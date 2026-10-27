using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunPhilosopherRerollRecipeChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, Player player, NEventRoom eventNode, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        string receipt = Path.Combine(output, "philosopher-reroll-recipe-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        string original = signature();
        var originalEvent = PredictorOperationCapture.ReadNullable(eventNode, "_event");
        var originalCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
        var originalDiscovered = player.DiscoveredCards.ToArray();
        var originalPotions = player.PotionSlots.ToArray();
        bool originalPotionUse = player.CanUseOrRemovePotions;
        var originalPlayerRng = player.PlayerRng.ToSerializable();
        var originalRunRng = run.Rng.ToSerializable();
        var originalOdds = player.PlayerOdds.ToSerializable();
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var originalHistory = history.Select(act => act.ToArray()).ToArray();
        var evidence = new List<object>();
        var initialOrdinalCards = new Dictionary<(int Rarity, int Ordinal), string>();
        var visuals = new Harmony("rt2.philosopher.reroll.recipe.native.visuals");
        EventModel? philosophers = null;
        EventRoom? room = null;
        RewardsSet? rewards = null;
        NRewardsScreen? rewardScreen = null;
        NCardRewardSelectionScreen? cardScreen = null;
        Task? offering = null;
        Task<bool>? selecting = null;
        bool passed = false;
        string? failure = null;
        async Task Frames(int count = 3)
        { for (int frame = 0; frame < count; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        void SelectAlternative(NCardRewardSelectionScreen screen, string option)
        {
            var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(screen, "_extraOptions");
            int index = alternatives.ToList().FindIndex(alternative => alternative.OptionId.Equals(option, StringComparison.OrdinalIgnoreCase));
            check(index >= 0, "original philosopher picker exposes " + option);
            AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(screen, [index]);
        }
        static string Cards(IEnumerable<PredictorCard> cards) => JsonSerializer.Serialize(cards.Select(card => new
        {
            card.Key, card.Type, card.UpgradeLevel, card.Enchantment, card.EnchantmentAmount,
            card.IncreasedBlock, card.IncreasedDamage, card.PermanentCostOverride,
            card.HasEternalKeyword, card.HasExhaustKeyword, card.HasInnateKeyword, card.HasRetainKeyword
        }));
        void RemoveFixtureCards()
        {
            foreach (var card in player.Deck.Cards.Where(card => !originalCards.Contains(card)).ToArray())
                player.Deck.RemoveInternal(card, silent: true);
            foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !originalCards.Contains(card)).ToArray())
                run.RemoveCard(card);
        }
        try
        {
            check(player.Relics.OfType<Driftwood>().Any() && !player.Relics.OfType<PaelsWing>().Any(),
                "native philosopher fixture has Driftwood and stays within vanilla two-alternative limit");
            SaveManager.Instance.MarkFtueAsComplete("power_card_ftue");
            // Original deck-add hooks and reward completion run. Omit only
            // animations/previews requiring the full combat/global card UI.
            visuals.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.GetTweenForCardsChangingPiles),
                    [typeof(IEnumerable<CardPileAddResult>), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            foreach (var method in typeof(CardCmd).GetMethods().Where(method => method.Name == nameof(CardCmd.PreviewCardPileAdd)))
                visuals.Patch(method, prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            visuals.Patch(AccessTools.Method(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.GetCardHolder)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
            string? color = null;
            foreach (var order in orders)
            {
                RemoveFixtureCards();
                player.PlayerRng.LoadFromSerializable(originalPlayerRng);
                player.PlayerOdds.LoadFromSerializable(originalOdds);
                room = new EventRoom(ModelDb.Event<ColorfulPhilosophers>()); run.PushRoom(room);
                run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ColorfulPhilosophers>().Id);
                RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<ColorfulPhilosophers>());
                philosophers = RunManager.Instance.EventSynchronizer.GetLocalEvent();
                await Until(() => philosophers.CurrentOptions.Count > 0, "native philosopher options initialize");
                color ??= PredictorOperationEventCapture.Option(philosophers, philosophers.CurrentOptions[0]).Key;
                for (int attempt = 0; attempt < 64 && !philosophers.CurrentOptions.Any(option =>
                    PredictorOperationEventCapture.Option(philosophers, option).Key == color); attempt++)
                    set(philosophers, "_currentOptions", AccessTools.Method(typeof(ColorfulPhilosophers), "GenerateInitialOptions").Invoke(philosophers, null));
                var choice = philosophers.CurrentOptions.Single(option => PredictorOperationEventCapture.Option(philosophers, option).Key == color);
                set(eventNode, "_event", philosophers);
                // Execute its actual character-pool closure and OfferRewards,
                // rather than recreating three superficially similar rewards.
                offering = choice.Chosen();
                await Until(() => overlays.Peek() is NRewardsScreen || offering.IsCompleted,
                    "original philosopher OfferRewards opens its three-group reward screen");
                if (offering.IsFaulted) throw new InvalidOperationException("Native philosopher OfferRewards failed", offering.Exception);
                check(!offering.IsCompleted && overlays.Peek() is NRewardsScreen, "original philosopher offer remains suspended on real rewards");
                rewardScreen = (NRewardsScreen)overlays.Peek()!;
                rewards = PredictorOperationCapture.Read<RewardsSet>(rewardScreen, "_rewardsSet");
                var groups = rewards.Rewards.OfType<CardReward>().ToArray();
                CardRarity[] rarities = [CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare];
                check(groups.Length == 3 && groups.Select((reward, index) => reward.Cards.Count() == 3 &&
                    reward.Cards.All(card => card.Rarity == rarities[index])).All(value => value),
                    "actual philosopher OfferRewards creates Common/Uncommon/Rare triples");
                foreach (int index in order)
                {
                    var reward = groups[index];
                    selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
                    await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted,
                        "actual philosopher group opens the native picker");
                    if (selecting.IsFaulted) throw new InvalidOperationException("Native philosopher picker failed", selecting.Exception);
                    check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen,
                        "native philosopher group awaits actual player choice");
                    cardScreen = (NCardRewardSelectionScreen)overlays.Peek()!;
                    await Frames(8);
                    string before = signature();
                    var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    check(snapshot.Scene is PredictorOperationEventScene { Event.Entry: "COLORFUL_PHILOSOPHERS", RewardScene: not null },
                        "capture retains actual philosopher event ancestry");
                    var scene = ((PredictorOperationEventScene)snapshot.Scene).RewardScene!;
                    long rewardId = PredictorLiveRunCapture.Id(reward);
                    var captured = scene.Rewards.Single(row => row.Id == rewardId);
                    var options = PredictorOperationCapture.Read<CardCreationOptions>(reward, "<RerollOptions>k__BackingField");
                    var expectedPool = options.GetPossibleCards(player).Select(PredictorLiveRunCapture.Key).ToImmutableArray();
                    var recipe = captured.RerollRecipe;
                    check(recipe is { Odds: PredictorCardOdds.Uniform, CardCount: 3, NoCardPoolModifications: true,
                        NoRarityModification: true, EncounterSource: false, NoUpgradeRoll: false } &&
                        recipe.CardPoolOverride.SequenceEqual(expectedPool),
                        "captured fixed-rarity recipe preserves actual native filtered pool order and flags after prior group operations");
                    check(scene.Rewards.Where(row => row.Status == PredictorRewardStatus.Open && row.CanReroll)
                        .All(row => row.RerollRecipe != null), "all remaining rerollable philosopher groups retain concrete recipes");
                    if (evidence.Count == 0)
                    {
                        foreach (int targetIndex in new[] { 0, 1, 2 })
                        {
                            long targetId = PredictorLiveRunCapture.Id(groups[targetIndex]);
                            var selectedScene = scene with { SelectedRewardId = targetId,
                                Rewards = scene.Rewards.Select(row => row with { CardSelectionEntered = row.Id == targetId }).ToImmutableArray() };
                            var selectedSnapshot = snapshot with { Scene = ((PredictorOperationEventScene)snapshot.Scene) with { RewardScene = selectedScene } };
                            var ordinalPreview = SurfaceOperationPreview.BuildRewardAlternative(selectedSnapshot, targetId, "REROLL", null);
                            check(ordinalPreview.Faults.IsEmpty, "initial philosopher ordinal preview resolves every target without hidden failure");
                            foreach (var ordinalGroup in ordinalPreview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups))
                            {
                                int ordinal = int.Parse(ordinalGroup.Label!.Split(':')[1]);
                                initialOrdinalCards[(targetIndex, ordinal)] = Cards(ordinalGroup.Cards);
                            }
                        }
                        var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(cardScreen, "_extraOptions");
                        int rerollIndex = alternatives.ToList().FindIndex(alternative => alternative.OptionId == "REROLL");
                        var button = Descendants(cardScreen).OfType<NCardRewardAlternativeButton>().ElementAt(rerollIndex);
                        await exercise("philosopher-reroll-ordinal", button, button, "DRIFTWOOD",
                            capturedScene => capturedScene is PredictorOperationEventScene { Event.Entry: "COLORFUL_PHILOSOPHERS", RewardScene: not null },
                            preview => preview.OptionKey == "REROLL" && preview.Faults.IsEmpty &&
                                preview.Style is { Text: OperationTextMode.None, Cards: OperationCardLayout.Groups } &&
                                preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups)
                                    .Select(group => group.Label).SequenceEqual(new[] { "RewardRerollStep:1", "RewardRerollStep:2", "RewardRerollStep:3" }),
                            (view, _) => check(Descendants(view).OfType<Label>().Count(label => label.Text.Contains("次重掷", StringComparison.Ordinal)) == 3,
                                "philosopher native hover renderer captions all three reroll ordinals"), false);
                        RunPhilosopherEnchantmentProjectionChecks(snapshot, check);
                        int unknownFilterCalls = 0;
                        var unknownOptions = new CardCreationOptions(options.CardPools, options.Source, options.RarityOdds,
                            _ => { unknownFilterCalls++; throw new InvalidOperationException("Unknown filter must not execute during capture"); })
                            .WithFlags(CardCreationFlags.NoCardPoolModifications | CardCreationFlags.NoRarityModification);
                        var unknownReward = new CardReward(unknownOptions, 3, player);
                        try
                        {
                            check(PredictorOperationCapture.CaptureReward(unknownReward, snapshot.Context.Catalog!).RerollRecipe == null &&
                                unknownFilterCalls == 0, "unknown filter remains unimplemented without invocation or an expanded pool");
                        }
                        finally { unknownReward.OnSkipped(); }
                        var mutablePoolOptions = new CardCreationOptions(options.CardPools, options.Source, options.RarityOdds, options.CardPoolFilter);
                        var mutablePoolReward = new CardReward(mutablePoolOptions, 3, player);
                        try
                        {
                            check(PredictorOperationCapture.CaptureReward(mutablePoolReward, snapshot.Context.Catalog!).RerollRecipe == null,
                                "native fixed-rarity filter without pool/rarity protection cannot be frozen into a detached recipe");
                        }
                        finally { mutablePoolReward.OnSkipped(); }
                        check(signature() == before, "filter-boundary capture checks leave the original native offer and RNG unchanged");
                    }
                    var predicted = await Task.Run(() => PredictorOperation.Preview(snapshot, new RerollCardReward(rewardId)));
                    check(predicted.Disposition == PredictorInputResult.Accepted && signature() == before,
                        "detached philosopher reroll succeeds without mutating native state or RNG");
                    var expected = predicted.Rewards.Single(row => row.Id == rewardId).Cards;
                    SelectAlternative(cardScreen, "REROLL");
                    await Until(() => !reward.CanReroll && PredictorOperationCapture.Read<bool>(reward, "_hasBeenRerolled"),
                        "original philosopher REROLL callback regenerates actual cards");
                    var actual = reward.Cards.Select(card => PredictorLiveRunCapture.Card(card, snapshot.Context.Catalog!)).ToImmutableArray();
                    var after = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    check(Cards(expected) == Cards(actual) && reward.Cards.All(card => card.Rarity == rarities[index]),
                        "native philosopher reroll matches detached identity, upgrade and enchantment for this rarity and order");
                    check(initialOrdinalCards[(index, Array.IndexOf(order, index) + 1)] == Cards(actual),
                        "initial target ordinal projection matches native cards across all six actual group orders");
                    check(predicted.State.Streams.SequenceEqual(after.State.Streams) && predicted.State.Odds == after.State.Odds &&
                        predicted.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls ==
                            snapshot.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls + 6,
                        "native philosopher reroll matches all RNG cursors and odds with six Rewards draws");
                    evidence.Add(new { order, rarity = rarities[index].ToString(), ordinal = Array.IndexOf(order, index) + 1,
                        color, pool = expectedPool.Select(key => key.Entry), expected = Cards(expected), actual = Cards(actual),
                        beforeRewards = snapshot.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls,
                        afterRewards = after.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls });
                    // Take a real rerolled card so subsequent captures include
                    // completed groups and their remaining native siblings.
                    var holder = Descendants(cardScreen).OfType<NGridCardHolder>().First(holder => holder.CardModel == reward.Cards.First());
                    AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(cardScreen, [holder]);
                    await Until(() => selecting.IsCompleted, "native philosopher card take finishes the original group");
                    if (selecting.IsFaulted) throw new InvalidOperationException("Native philosopher card take failed", selecting.Exception);
                    check(selecting.Result && reward.SuccessfullySelected, "actual card take marks the philosopher group completed");
                    cardScreen = null; selecting = null;
                }
                await Until(() => offering.IsCompleted, "taking three native groups completes original philosopher event");
                if (offering.IsFaulted) throw new InvalidOperationException("Native philosopher event completion failed", offering.Exception);
                if (GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
                rewardScreen = null; rewards = null; offering = null;
                philosophers.EnsureCleanup(); philosophers = null;
                run.PopCurrentRoom(); room = null;
            }
            // Preserve the other official filtered recipe during the single
            // capture-owner cleanup: its actual Trade closure also subscribes
            // an AfterGenerated upgrade, while forbidding upgrade RNG rolls.
            foreach (var potion in player.Potions.ToArray()) player.DiscardPotionInternal(potion, silent: true);
            check(player.AddPotionInternal(ModelDb.Potion<WeakPotion>().ToMutable(), 0, silent: true).success &&
                player.AddPotionInternal(ModelDb.Potion<StrengthPotion>().ToMutable(), 1, silent: true).success,
                "Future of Potions fixture supplies two actual common potions");
            room = new EventRoom(ModelDb.Event<TheFutureOfPotions>()); run.PushRoom(room);
            run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<TheFutureOfPotions>().Id);
            RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<TheFutureOfPotions>());
            philosophers = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            await Until(() => philosophers.CurrentOptions.Count > 0, "native Future of Potions trade options initialize");
            set(eventNode, "_event", philosophers);
            offering = philosophers.CurrentOptions[0].Chosen();
            await Until(() => overlays.Peek() is NRewardsScreen || offering.IsCompleted,
                "original Future of Potions Trade opens its native card reward");
            if (offering.IsFaulted) throw new InvalidOperationException("Native Future of Potions Trade failed", offering.Exception);
            check(!offering.IsCompleted && overlays.Peek() is NRewardsScreen && player.Potions.Count() == 1,
                "original Trade discards its actual potion and awaits the generated reward");
            rewardScreen = (NRewardsScreen)overlays.Peek()!;
            rewards = PredictorOperationCapture.Read<RewardsSet>(rewardScreen, "_rewardsSet");
            var futureReward = rewards.Rewards.OfType<CardReward>().Single();
            selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(futureReward);
            await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted,
                "original Future of Potions reward opens its native card picker");
            check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen, "Future of Potions native picker awaits choice");
            cardScreen = (NCardRewardSelectionScreen)overlays.Peek()!;
            await Frames(8);
            string futureBefore = signature();
            var futureSnapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var futureScene = futureSnapshot.Scene as PredictorOperationEventScene;
            check(futureScene is { Event.Entry: "THE_FUTURE_OF_POTIONS", RewardScene: not null },
                "Future of Potions capture retains its real event ancestry");
            long futureId = PredictorLiveRunCapture.Id(futureReward);
            var futureRecipe = futureScene!.RewardScene!.Rewards.Single(row => row.Id == futureId).RerollRecipe;
            var futureOptions = PredictorOperationCapture.Read<CardCreationOptions>(futureReward, "<RerollOptions>k__BackingField");
            var futurePool = futureOptions.GetPossibleCards(player).Select(PredictorLiveRunCapture.Key).ToImmutableArray();
            check(futureRecipe is { CardCount: 3, Odds: PredictorCardOdds.Uniform, NoUpgradeRoll: true,
                    NoCardPoolModifications: true, NoRarityModification: true, UpgradeAfterGeneration: true } &&
                futureRecipe.CardPoolOverride.SequenceEqual(futurePool),
                "single capture owner preserves native Future of Potions filtered pool, flags and subscribed post-generation upgrade");
            int externalCallbackCalls = 0;
            Action externalCallback = () => { externalCallbackCalls++; throw new InvalidOperationException("External callback must not run during capture"); };
            futureReward.AfterGenerated += externalCallback;
            try
            {
                check(PredictorOperationCapture.CaptureReward(futureReward, futureSnapshot.Context.Catalog!).RerollRecipe is
                    { UpgradeAfterGeneration: true, NoUpgradeRoll: true } && externalCallbackCalls == 0,
                    "external AfterGenerated hook is omitted from the vanilla model without execution or blocking the official recipe");
            }
            finally { futureReward.AfterGenerated -= externalCallback; }
            var futurePredicted = await Task.Run(() => PredictorOperation.Preview(futureSnapshot, new RerollCardReward(futureId)));
            check(futurePredicted.Disposition == PredictorInputResult.Accepted && signature() == futureBefore,
                "detached Future of Potions reroll preserves original native state and RNG");
            var futureExpected = futurePredicted.Rewards.Single(row => row.Id == futureId).Cards;
            SelectAlternative(cardScreen, "REROLL");
            await Until(() => !futureReward.CanReroll && PredictorOperationCapture.Read<bool>(futureReward, "_hasBeenRerolled"),
                "original Future of Potions REROLL callback regenerates and upgrades its real cards");
            var futureActual = futureReward.Cards.Select(card => PredictorLiveRunCapture.Card(card, futureSnapshot.Context.Catalog!)).ToImmutableArray();
            var futureAfter = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            check(Cards(futureExpected) == Cards(futureActual) && futureActual.All(card => card.UpgradeLevel > 0) &&
                futurePredicted.State.Streams.SequenceEqual(futureAfter.State.Streams) && futurePredicted.State.Odds == futureAfter.State.Odds &&
                futureAfter.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls ==
                    futureSnapshot.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls + 3,
                "native Future of Potions reroll matches fully upgraded cards, all RNG cursors and odds using three Rewards draws");
            evidence.Add(new { source = "THE_FUTURE_OF_POTIONS", expected = Cards(futureExpected), actual = Cards(futureActual),
                pool = futurePool.Select(key => key.Entry),
                beforeRewards = futureSnapshot.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls,
                afterRewards = futureAfter.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls });
            SelectAlternative(cardScreen, "Skip");
            await Until(() => selecting.IsCompleted, "native Future of Potions picker closes through original skip control");
            check(selecting.IsCompletedSuccessfully, "native Future of Potions picker cleanup succeeds");
            cardScreen = null; selecting = null;
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            await Until(() => offering.IsCompleted, "native Future of Potions Trade completes after reward set skip");
            if (offering.IsFaulted) throw new InvalidOperationException("Native Future of Potions completion failed", offering.Exception);
            if (GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
            rewardScreen = null; rewards = null; offering = null;
            philosophers.EnsureCleanup(); philosophers = null;
            run.PopCurrentRoom(); room = null;
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (cardScreen != null && GodotObject.IsInstanceValid(cardScreen) && selecting is { IsCompleted: false })
            {
                PredictorOperationCapture.Read<TaskCompletionSource<int?>>(cardScreen, "_completionSource").TrySetResult(null);
                await Until(() => selecting.IsCompleted, "philosopher fixture cleanup closes its pending native picker");
            }
            if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards))
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (rewardScreen != null && GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
            if (offering is { IsCompleted: false }) await Until(() => offering.IsCompleted, "philosopher fixture cleanup unwinds original event offer");
            if (offering?.IsFaulted == true) _ = offering.Exception;
            philosophers?.EnsureCleanup();
            visuals.UnpatchAll(visuals.Id);
            set(eventNode, "_event", originalEvent);
            if (room != null && ReferenceEquals(run.CurrentRoom, room)) run.PopCurrentRoom();
            RemoveFixtureCards();
            foreach (var potion in player.Potions.ToArray()) player.DiscardPotionInternal(potion, silent: true);
            for (int slot = 0; slot < originalPotions.Length; slot++)
                if (originalPotions[slot] is { } potion) player.AddPotionInternal(potion, slot, silent: true);
            player.CanUseOrRemovePotions = originalPotionUse;
            set(player, nameof(Player.DiscoveredCards), originalDiscovered.ToList());
            player.PlayerRng.LoadFromSerializable(originalPlayerRng); player.PlayerOdds.LoadFromSerializable(originalOdds);
            run.Rng.LoadFromSerializable(originalRunRng);
            history.Clear(); foreach (var act in originalHistory) history.Add(act.ToList());
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, "philosopher-reroll-restore-before.json"), original);
                File.WriteAllText(Path.Combine(output, "philosopher-reroll-restore-after.json"), signature());
            }
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, restored, failure, evidence,
                source = "Original ColorfulPhilosophers option closure, OfferRewards, three native card rewards, all six rarity orders, actual REROLL callback and native card take; original Future of Potions Trade/filter/AfterGenerated upgrade/REROLL. Only card motion/preview omitted; no fabricated recipe or event parent. No natural-seed or Owner-runtime acceptance claim." },
                new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "philosopher recipe fixture restores original run/player/RNG/odds/history");
        }
    }

    private static void RunPhilosopherEnchantmentProjectionChecks(PredictorOperationSnapshot original,
        Action<bool, string> check)
    {
        // An isolated projection fixture, not a natural seed: legal fixed
        // Ironclad candidate subsets make Lens/Wing's zero/one Niche draw
        // distinction explicit without a seed search or native-run mutation.
        var eventScene = (PredictorOperationEventScene)original.Scene;
        var nativeRows = eventScene.RewardScene!.Rewards.Where(row => row.Kind == PredictorRewardKind.Card).ToArray();
        string[][] keys = [["SHRUG_IT_OFF", "IRON_WAVE", "TRUE_GRIT"],
            ["INFLAME", "UPPERCUT", "BATTLE_TRANCE"], ["DEMON_FORM", "FEED", "OFFERING"]];
        var catalog = original.Context.Catalog!;
        var rows = nativeRows.Select((row, index) => row with
        {
            CardSelectionEntered = index == 2,
            RerollRecipe = row.RerollRecipe! with { CardPoolOverride = keys[index].Select(entry =>
                catalog.CardDefinitions.Single(definition => definition.Prototype.Key.Entry == entry).Prototype.Key).ToImmutableArray() }
        }).ToImmutableArray();
        long lensId = original.State.NextInstanceId, wingId = lensId + 1;
        var relics = ImmutableArray.Create(new PredictorRelic(lensId, new("RELIC", "FRESNEL_LENS"), 0),
            new PredictorRelic(wingId, new("RELIC", "WING_CHARM"), 0))
            .AddRange(original.State.Relics.Where(relic => relic.Key.Entry == "DRIFTWOOD"));
        var controlled = original with
        {
            Context = original.Context with { Character = new("CHARACTER", "SILENT") },
            State = original.State with { NextInstanceId = wingId + 1, Relics = relics },
            Scene = eventScene with { RewardScene = eventScene.RewardScene with { Rewards = rows, SelectedRewardId = rows[2].Id } }
        };
        string originalScene = PredictorOperationCapture.SceneFingerprint(original.Scene);
        var originalStreams = original.State.Streams;
        static string BaseCards(IEnumerable<PredictorCard> cards) => string.Join(";", cards.Select(card =>
            card.Key.Serialized + ":" + card.UpgradeLevel));
        static ImmutableArray<OperationPreviewCardGroup> Project(PredictorOperationSnapshot snapshot, long target)
        {
            var preview = SurfaceOperationPreview.BuildRewardAlternative(snapshot, target, "REROLL", null);
            if (!preview.Faults.IsEmpty) throw new InvalidOperationException("Controlled philosopher projection failed", preview.Faults[0].Exception);
            return preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).ToImmutableArray();
        }
        bool observed = false;
        for (int offset = 0; offset < 12 && !observed; offset++)
        {
            var niche = controlled.State.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Restore();
            for (int skip = 0; skip < offset; skip++) niche.NextFloat();
            var sample = controlled with { State = controlled.State with { Streams = controlled.State.Streams.Select(stream =>
                stream.Stream == PredictorStream.Niche ? PredictorStreamState.Capture(PredictorStream.Niche, niche) : stream).ToImmutableArray() } };
            PredictorOperationResult After(int previous) => PredictorOperation.Preview(sample,
                [new ExitCardSelection(), new EnterCardSelection(rows[previous].Id), new RerollCardReward(rows[previous].Id),
                    new ExitCardSelection(), new EnterCardSelection(rows[2].Id), new RerollCardReward(rows[2].Id)]);
            var afterCommon = After(0); var afterUncommon = After(1);
            check(afterCommon.Disposition == PredictorInputResult.Accepted && afterUncommon.Disposition == PredictorInputResult.Accepted,
                "controlled philosopher prefixes are accepted by the existing numerical runtime");
            var a = afterCommon.Rewards.Single(row => row.Id == rows[2].Id).Cards;
            var b = afterUncommon.Rewards.Single(row => row.Id == rows[2].Id).Cards;
            check(BaseCards(a) == BaseCards(b) && afterUncommon.State.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls ==
                afterCommon.State.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls + 1,
                "Lens before Wing leaves common prefix at zero Niche draws and nonblock uncommon prefix at one, preserving target identity and upgrades");
            if (!a.Where((card, slot) => card.Enchantment != b[slot].Enchantment).Any()) continue;
            observed = true;
            var groups = Project(sample, rows[2].Id);
            var second = groups.Single(group => group.Label == "RewardRerollStep:2").Cards;
            check(BaseCards(second) == BaseCards(a), "ambiguous enchantment projection preserves every target identity and upgrade");
            for (int slot = 0; slot < second.Length; slot++)
            {
                bool differs = a[slot].Enchantment != b[slot].Enchantment || a[slot].EnchantmentAmount != b[slot].EnchantmentAmount;
                check(differs ? second[slot].Enchantment == null && second[slot].EnchantmentAmount == 0 :
                    second[slot].Enchantment == a[slot].Enchantment && second[slot].EnchantmentAmount == a[slot].EnchantmentAmount,
                    "only actual same-ordinal enchantment disagreement is omitted");
            }
            var first = PredictorOperation.Preview(sample, new RerollCardReward(rows[2].Id)).Rewards.Single(row => row.Id == rows[2].Id).Cards;
            check(groups[0].Cards.SequenceEqual(first), "first philosopher ordinal retains its complete original numerical cards");
            var commonScene = (PredictorOperationEventScene)sample.Scene;
            var commonSnapshot = sample with { Scene = commonScene with { RewardScene = commonScene.RewardScene! with
            {
                SelectedRewardId = rows[0].Id,
                Rewards = rows.Select(row => row with { CardSelectionEntered = row.Id == rows[0].Id }).ToImmutableArray()
            } } };
            check(Project(commonSnapshot, rows[0].Id).All(group => group.Cards.All(card =>
                card.Enchantment?.Entry == "NIMBLE" && card.EnchantmentAmount == 2)),
                "holding Lens and Wing preserves certain Nimble on all three common ordinals");
        }
        check(observed, "bounded twelve-position Niche fixture demonstrates an actual same-ordinal enchantment disagreement");
        check(PredictorOperationCapture.SceneFingerprint(original.Scene) == originalScene && original.State.Streams.SequenceEqual(originalStreams),
            "controlled enchantment projection leaves original native snapshot and streams untouched");
    }
}
