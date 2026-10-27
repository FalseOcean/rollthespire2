using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunRewardSequenceChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, Player player, NGlobalUi global, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        // Original merchant Orrery purchase supplies the legal five-group source. The
        // detached recipe variants below are explicitly separate evidence.
        foreach (bool sacrifice in new[] { false, true })
            await RunNativeRewardSequence(sacrifice);

        async Task RunNativeRewardSequence(bool sacrifice)
        {
            string caseName = sacrifice ? "reward-sequence-sacrifice" : "reward-sequence-reroll";
            string action = sacrifice ? "SACRIFICE" : "REROLL";
            string original = signature();
            string receipt = Path.Combine(output, caseName + "-result.json");
            File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
            var oldRelics = player.Relics.ToArray();
            var relicList = PredictorOperationCapture.Read<List<RelicModel>>(player, "_relics");
            var oldDiscoveredCards = player.DiscoveredCards.ToArray();
            var oldDiscoveredRelics = player.DiscoveredRelics.ToArray();
            var oldCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
            var oldRng = player.PlayerRng.ToSerializable();
            var oldOdds = player.PlayerOdds.ToSerializable();
            var oldRunRng = run.Rng.ToSerializable();
            var oldPersonalBag = player.RelicGrabBag.ToSerializable();
            var oldSharedBag = run.SharedRelicGrabBag.ToSerializable();
            int oldGold = player.Gold;
            var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
            var oldHistory = history.Select(act => act.ToArray()).ToArray();
            var oldInventory = global.RelicInventory;
            var inventory = new NRelicInventory();
            var visuals = new Harmony("rt2.reward.sequence.native.visuals");
            var omitted = new List<(int Index, RelicModel Relic)>();
            NRewardsScreen? rewardScreen = null;
            NCardRewardSelectionScreen? cardScreen = null;
            RewardsSet? rewards = null;
            MerchantRoom? merchant = null;
            Task<bool>? purchase = null;
            Task<bool>? selecting = null;
            bool passed = false;
            string? failure = null;
            object? evidence = null;
            async Task Frames(int count = 3)
            { for (int frame = 0; frame < count; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
            async Task Until(Func<bool> ready, string reason)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
                check(ready(), reason);
            }
            int AlternativeIndex(string option) => PredictorOperationCapture
                .Read<IReadOnlyList<CardRewardAlternative>>(cardScreen!, "_extraOptions").ToList()
                .FindIndex(alternative => alternative.OptionId.Equals(option, StringComparison.OrdinalIgnoreCase));
            void SelectAlternative(string option)
            {
                int index = AlternativeIndex(option);
                check(index >= 0, caseName + " original picker exposes " + option);
                AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(cardScreen, [index]);
            }
            async Task Open(CardReward reward)
            {
                selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
                await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted,
                    caseName + " original reward selection opens its native picker");
                if (selecting.IsFaulted) throw new InvalidOperationException(caseName + " native selection failed", selecting.Exception);
                check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen,
                    caseName + " selection remains pending for actual player input");
                cardScreen = (NCardRewardSelectionScreen)overlays.Peek()!;
                await Frames(40);
            }
            try
            {
                check(!player.Relics.OfType<Orrery>().Any() && player.Relics.OfType<Driftwood>().Any() &&
                    player.Relics.OfType<TheCourier>().Any(),
                    caseName + " starts with the host Courier/Driftwood and no Orrery");
                if (sacrifice)
                {
                    // Preserve the original object and hook-removal flag. The
                    // vanilla three-alternative limitation remains unchanged.
                    foreach (var relic in relicList.Where(relic => relic is Driftwood).ToArray())
                    { omitted.Add((relicList.IndexOf(relic), relic)); relicList.Remove(relic); }
                    var wing = (PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable();
                    wing.RewardsSacrificed = 1;
                    player.AddRelicInternal(wing, silent: true);
                    // Only permute legal available bag members. No pickup
                    // effect is bypassed, and native rarity/RNG/pull/obtain all
                    // execute. Keeping these fixture pulls passive permits a
                    // strict restoration of the host's existing deck and HP.
                    var deques = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(player.RelicGrabBag, "_deques");
                    foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare })
                    {
                        var deque = deques[rarity];
                        var safe = deque.Where(relic => !relic.HasUponPickupEffect && relic.IsAllowed(run) &&
                            !player.Relics.Any(owned => owned.Id == relic.Id)).Take(3).ToArray();
                        check(safe.Length == 3, caseName + " has three legal passive " + rarity + " bag members");
                        foreach (var relic in safe) deque.Remove(relic);
                        deque.InsertRange(0, safe);
                    }
                }
                set(global, nameof(NGlobalUi.RelicInventory), inventory);
                visuals.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
                SaveManager.Instance.MarkFtueAsComplete("power_card_ftue");
                var shopBag = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(player.RelicGrabBag, "_deques")[RelicRarity.Shop];
                var canonicalOrrery = shopBag.OfType<Orrery>().Single();
                check(canonicalOrrery.IsAllowedInShops && canonicalOrrery.IsAllowed(run),
                    caseName + " Orrery is an available legal Shop-bag member");
                shopBag.Remove(canonicalOrrery); shopBag.Add(canonicalOrrery);
                merchant = new MerchantRoom(); run.PushRoom(merchant); set(merchant, "_runState", run);
                run.AppendToMapPointHistory(MapPointType.Shop, RoomType.Shop, null);
                player.Gold = 999;
                var merchantInventory = MerchantInventory.CreateForNormalMerchant(player);
                merchant.Inventories.Add(merchantInventory);
                var entry = merchantInventory.RelicEntries[2];
                check(entry.Model is Orrery, caseName + " original normal merchant supplies Orrery in the Shop slot");
                long entryId = PredictorLiveRunCapture.Id(entry);
                purchase = entry.OnTryPurchaseWrapper(merchantInventory);
                await Until(() => overlays.Peek() is NRewardsScreen || purchase.IsCompleted,
                    caseName + " original Orrery purchase opens its original five rewards");
                if (purchase.IsFaulted) throw new InvalidOperationException(caseName + " Orrery purchase failed", purchase.Exception);
                check(!purchase.IsCompleted && overlays.Peek() is NRewardsScreen,
                    caseName + " original purchase remains suspended at its child rewards");
                rewardScreen = (NRewardsScreen)overlays.Peek()!;
                rewards = PredictorOperationCapture.Read<RewardsSet>(rewardScreen, "_rewardsSet");
                var cards = rewards.Rewards.OfType<CardReward>().ToArray();
                check(cards.Length == 5, caseName + " original Orrery generated five independent groups");
                await Open(cards[0]);
                var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                check(snapshot.Scene is PredictorOperationObtainScene { Source.Entry: "ORRERY", Kind: PredictorOperationObtainChildKind.Rewards,
                    Parent: PredictorOperationShopScene { PendingPurchase: { } pending } } && pending.EntryId == entryId,
                    caseName + " capture retains the actual suspended merchant purchase and exact entry identity");
                long firstId = PredictorLiveRunCapture.Id(cards[0]);
                long? wingId = sacrifice ? PredictorLiveRunCapture.Id(player.Relics.OfType<PaelsWing>().Single()) : null;
                string liveBefore = signature();
                string snapshotBefore = JsonSerializer.Serialize(snapshot.State) + PredictorOperationCapture.SceneFingerprint(snapshot.Scene);
                var preview = SurfaceOperationPreview.BuildRewardAlternative(snapshot, firstId, action, wingId);
                check(preview.Faults.IsEmpty && preview.PendingReasons.IsEmpty, caseName + " initial complete forecast has no fault or missing input");
                var expectedGroups = RerollGroups(preview);
                var expectedRelics = SacrificeRelics(preview);
                check(sacrifice ? expectedRelics.Length == 3 && preview.Notices.Contains("RewardSacrificeProgress:1") :
                    expectedGroups.Length == 5 && expectedGroups.Select(group => group.Label)
                        .SequenceEqual(Enumerable.Range(1, 5).Select(step => "RewardRerollStep:" + step)),
                    caseName + " projects the actual full remaining sequence and original pre-action parity");
                if (!sacrifice) CheckDetachedRecipeVariants(snapshot, firstId, check);
                else CheckDetachedSacrificeVariants(snapshot, firstId, wingId!.Value, check);
                check(signature() == liveBefore && snapshotBefore == JsonSerializer.Serialize(snapshot.State) +
                    PredictorOperationCapture.SceneFingerprint(snapshot.Scene), caseName + " forecasting preserves live run/RNG and its detached source snapshot");
                var button = Descendants(cardScreen!).OfType<NCardRewardAlternativeButton>().ElementAt(AlternativeIndex(action));
                await exercise(caseName, button, button, sacrifice ? "PAELS_WING" : "DRIFTWOOD",
                    captured => captured is PredictorOperationObtainScene { Source.Entry: "ORRERY", Kind: PredictorOperationObtainChildKind.Rewards,
                        Parent: PredictorOperationShopScene { PendingPurchase: { } parent } } && parent.EntryId == entryId,
                    hovered => sacrifice ? hovered.Notices.Contains("RewardSacrificeProgress:1") && SacrificeRelics(hovered).SequenceEqual(expectedRelics) :
                        RerollGroups(hovered).Length == 5 && RerollGroups(hovered).Zip(expectedGroups)
                            .All(pair => SameCards(pair.First.Cards, pair.Second.Cards)),
                    (view, hovered) => { if (!sacrifice) CheckEarliestRewardCardStacks(view, hovered, check); }, false);

                if (!sacrifice)
                {
                    SelectAlternative("Skip");
                    await Until(() => selecting!.IsCompleted, caseName + " original Skip exits an untouched group");
                    check(selecting!.IsCompletedSuccessfully && cards[0].CanReroll && signature() == liveBefore,
                        caseName + " native Skip before any reroll consumes neither the group nor RNG");
                    await Open(cards[0]);
                    var reopened = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    var reopenedPreview = SurfaceOperationPreview.BuildRewardAlternative(reopened, firstId, "REROLL", null);
                    check(RerollGroups(reopenedPreview).Length == 5 && RerollGroups(reopenedPreview).Zip(expectedGroups)
                        .All(pair => SameCards(pair.First.Cards, pair.Second.Cards)),
                        caseName + " reopening after ordinary Skip preserves all five exact future rerolls");
                }

                var inputs = ImmutableArray<PredictorInput>.Empty;
                var actualCards = new List<string[]>();
                var actualRelics = new List<ModelKey>();
                int steps = sacrifice ? cards.Length : 2;
                for (int step = 0; step < steps; step++)
                {
                    long id = PredictorLiveRunCapture.Id(cards[step]);
                    if (step > 0)
                    {
                        await Open(cards[step]);
                        inputs = inputs.Add(new EnterCardSelection(id));
                    }
                    inputs = inputs.Add(sacrifice ? new SacrificeCardReward(id, wingId!.Value) : new RerollCardReward(id));
                    var expected = PredictorOperation.Preview(snapshot, inputs);
                    check(expected.Disposition == PredictorInputResult.Accepted,
                        caseName + " ordered real-operation path accepts step " + (step + 1));
                    var beforeRelics = player.Relics.ToArray();
                    SelectAlternative(action);
                    if (sacrifice)
                    {
                        await Until(() => selecting!.IsCompleted, caseName + " original sacrifice settles step " + (step + 1));
                        check(selecting!.IsCompletedSuccessfully, caseName + " original sacrifice has no hidden selection fault");
                        actualRelics.AddRange(player.Relics.Where(relic => !beforeRelics.Contains(relic)).Select(PredictorLiveRunCapture.Key));
                        check(player.Relics.OfType<PaelsWing>().Single().RewardsSacrificed == step + 2 &&
                            actualRelics.SequenceEqual(expected.State.Relics.Where(relic => !snapshot.State.Relics.Any(old => old.Id == relic.Id)).Select(relic => relic.Key)),
                            caseName + " every cumulative native pull matches the exact ordered pipeline");
                    }
                    else
                    {
                        await Until(() => !cards[step].CanReroll, caseName + " original reroll executes step " + (step + 1));
                        await Frames(4);
                        var native = cards[step].Cards.Select(card => PredictorLiveRunCapture.Card(card, snapshot.Context.Catalog!)).ToImmutableArray();
                        var ordered = expected.Rewards.Single(reward => reward.Id == id).Cards;
                        var afterNative = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                        File.WriteAllText(Path.Combine(output, caseName + "-step-" + (step + 1) + "-cards.json"),
                            JsonSerializer.Serialize(new { step = step + 1, native, ordered, displayed = expectedGroups[step].Cards,
                                nativeMatchesOrdered = SameCards(native, ordered), nativeMatchesDisplay = SameCards(native, expectedGroups[step].Cards),
                                comparison = "Complete PredictorCard record; only allocation Id and not-yet-deck-entered FloorAdded normalized",
                                normalizedNative = native.Select(card => card with { Id = 0, FloorAdded = 0 }),
                                normalizedExpected = ordered.Select(card => card with { Id = 0, FloorAdded = 0 }),
                                nativeStreams = afterNative.State.Streams, expectedStreams = expected.State.Streams,
                                nativeOdds = afterNative.State.Odds, expectedOdds = expected.State.Odds,
                                nativeSource = afterNative.Scene.GetType().Name, inputs }, new JsonSerializerOptions { WriteIndented = true }));
                        check(SameCards(native, ordered) && SameCards(native, expectedGroups[step].Cards),
                            caseName + " native identities, upgrades and enchantments match displayed ordinal " + (step + 1));
                        check(afterNative.State.Streams.SequenceEqual(expected.State.Streams) && afterNative.State.Odds == expected.State.Odds,
                            caseName + " native RNG streams and rarity odds match the exact ordered reroll state at ordinal " + (step + 1));
                        actualCards.Add(native.Select(card => card.Key.Entry).ToArray());
                        SelectAlternative("Skip"); // Native Skip exits without consuming this reward.
                        await Until(() => selecting!.IsCompleted, caseName + " native ordinary exit returns to remaining rewards");
                        check(selecting!.IsCompletedSuccessfully, caseName + " ordinary exit succeeds");
                        inputs = inputs.Add(new ExitCardSelection());
                        if (step == 0)
                        {
                            var nextCapture = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                            var nextId = PredictorLiveRunCapture.Id(cards[1]);
                            var entered = PredictorOperation.Preview(nextCapture, new EnterCardSelection(nextId));
                            check(entered.Disposition == PredictorInputResult.Accepted, caseName + " first exit leaves the next independent reward available");
                        }
                    }
                }
                if (sacrifice)
                {
                    check(actualRelics.SequenceEqual(expectedRelics), caseName + " all three displayed gifts equal five genuine consecutive sacrifices");
                    await Frames(65); // Original Wing activation animation resets its transient flag.
                }
                else
                {
                    await Open(cards[2]);
                    var remaining = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    var remainingPreview = SurfaceOperationPreview.BuildRewardAlternative(remaining, PredictorLiveRunCapture.Id(cards[2]), "REROLL", null);
                    check(RerollGroups(remainingPreview).Length == 3,
                        caseName + " already rerolled groups disappear while ordinary Skip did not consume remaining rerolls");
                    SelectAlternative("Skip");
                    await Until(() => selecting!.IsCompleted, caseName + " final untouched picker exits ordinarily");
                }
                if (!RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards))
                    RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                if (GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
                await Until(() => purchase.IsCompleted, caseName + " original merchant purchase unwinds after native reward completion");
                check(purchase.IsCompletedSuccessfully && await purchase && ReferenceEquals(run.CurrentRoom, merchant) && entry.Model is not Orrery,
                    caseName + " genuine purchase returns to the same shop and performs its Courier restock");
                var completedCapture = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                check(completedCapture.Scene is PredictorOperationShopScene { PendingPurchase: null },
                    caseName + " native capture after sequence completion is the settled shop rather than a fabricated reward continuation");
                evidence = new { nativeGroups = cards.Length, nativeActions = steps, actualCards,
                    actualRelics = actualRelics.Select(key => key.Entry).ToArray(), detachedRecipeVariants = !sacrifice,
                    originalOrreryMerchantPurchase = true, entryId, nativeHoverCaptureWorkerRenderer = true };
                passed = true;
            }
            catch (Exception error) { failure = error.ToString(); throw; }
            finally
            {
                if (cardScreen != null && GodotObject.IsInstanceValid(cardScreen) && selecting is { IsCompleted: false })
                {
                    PredictorOperationCapture.Read<TaskCompletionSource<int?>>(cardScreen, "_completionSource").TrySetResult(null);
                    await Until(() => selecting.IsCompleted, caseName + " cleanup unwinds pending native selection");
                }
                if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards))
                    RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                if (rewardScreen != null && GodotObject.IsInstanceValid(rewardScreen) && rewardScreen.GetParent() != null) overlays.Remove(rewardScreen);
                if (purchase is { IsCompleted: false }) await Until(() => purchase.IsCompleted, caseName + " cleanup unwinds original purchase");
                if (purchase?.IsFaulted == true) _ = purchase.Exception;
                visuals.UnpatchAll(visuals.Id);
                set(global, nameof(NGlobalUi.RelicInventory), oldInventory); inventory.Free();
                foreach (var relic in player.Relics.Where(relic => !oldRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
                foreach (var (index, relic) in omitted.OrderBy(item => item.Index)) if (!relicList.Contains(relic)) relicList.Insert(index, relic);
                foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !oldCards.Contains(card)).ToArray()) run.RemoveCard(card);
                player.Gold = oldGold;
                set(player, nameof(Player.DiscoveredCards), oldDiscoveredCards.ToList());
                set(player, nameof(Player.DiscoveredRelics), oldDiscoveredRelics.ToList());
                player.PlayerRng.LoadFromSerializable(oldRng); player.PlayerOdds.LoadFromSerializable(oldOdds);
                run.Rng.LoadFromSerializable(oldRunRng);
                player.RelicGrabBag.LoadFromSerializable(oldPersonalBag); run.SharedRelicGrabBag.LoadFromSerializable(oldSharedBag);
                if (merchant != null && ReferenceEquals(run.CurrentRoom, merchant)) run.PopCurrentRoom();
                history.Clear(); foreach (var act in oldHistory) history.Add(act.ToList());
                bool restored = signature() == original;
                if (!restored)
                {
                    File.WriteAllText(Path.Combine(output, caseName + "-restore-before.json"), original);
                    File.WriteAllText(Path.Combine(output, caseName + "-restore-after.json"), signature());
                }
                File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, restored, failure, evidence,
                    source = "Controlled legal Shop-bag permutation, original normal merchant inventory and Orrery purchase, original five reward generation, native picker/alternative callback and sequential settlement. Actual captured Shop PendingPurchase ancestry; passive legal sacrifice bag permutation; visual relic inventory animation omitted. Recipe variants are detached, not natural seeds or Owner acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
                if (passed) check(restored, caseName + " restores the complete original player/run/RNG/bags signature");
            }
        }
    }

    private static OperationPreviewCardGroup[] RerollGroups(OperationPreview preview) => preview.Branches
        .SelectMany(branch => branch.Sections).Where(section => section.Source.Entry == "DRIFTWOOD")
        .SelectMany(section => section.CardGroups).ToArray();

    private static void CheckEarliestRewardCardStacks(NeowOperationOverlay view, OperationPreview preview,
        Action<bool, string> check)
    {
        var groups = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).ToArray();
        var stacks = Descendants(view).OfType<Control>().Where(control =>
            control.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
        check(stacks.Length == groups.Length && groups.All(group => group.EarliestFirst),
            "reroll ordinal groups explicitly retain first-card-front presentation in every native stack");
        for (int index = 0; index < groups.Length; index++)
        {
            var cards = Descendants(stacks[index]).OfType<NCard>().ToArray();
            var tiles = cards.Select(card => card.GetParent<Control>()).ToArray();
            check(cards.Select(card => card.Model!.Id.Entry).SequenceEqual(groups[index].Cards.Select(card => card.Key.Entry)),
                "reroll native stack keeps original card-key array order for group " + (index + 1));
            check(tiles.Length > 1 && stacks[index].GetMeta("rt2_stacked").AsBool() &&
                tiles[0].Modulate == Colors.White && tiles.Skip(1).All(tile =>
                    tiles[0].Position.X < tile.Position.X && tiles[0].Position.Y > tile.Position.Y &&
                    tiles[0].ZIndex > tile.ZIndex && tile.Modulate.R < tiles[0].Modulate.R &&
                    tile.Modulate.G < tiles[0].Modulate.G && tile.Modulate.B < tiles[0].Modulate.B),
                "reroll group's first native card is lowest-left, at highest ZIndex and brightest for group " + (index + 1));
        }
    }

    private static ModelKey[] SacrificeRelics(OperationPreview preview) => preview.Branches.SelectMany(branch => branch.Sections)
        .Where(section => section.Source.Entry == "PAELS_WING").SelectMany(section => section.Rows)
        .Where(row => row.Action == OperationPreviewAction.ObtainRelic).SelectMany(row => row.Models).ToArray();

    // Catalog prototypes use FloorAdded=1, while an original not-yet-picked
    // native reward has no FloorAddedToDeck (captured as 0). The deck-entry
    // field is assigned by native CardPileCmd only upon taking a card. Preserve
    // every other field, including removability/removal flags and dynamic state.
    private static bool SameCards(IEnumerable<PredictorCard> left, IEnumerable<PredictorCard> right) =>
        left.Select(card => card with { Id = 0, FloorAdded = 0 })
            .SequenceEqual(right.Select(card => card with { Id = 0, FloorAdded = 0 }));

    private static PredictorOperationSnapshot DetachedRewardRows(PredictorOperationSnapshot snapshot,
        ImmutableArray<PredictorReward> rewards, long selectedId) => snapshot with
        { Scene = new PredictorOperationRewardsScene(rewards, selectedId),
            State = snapshot.State with { NextInstanceId = Math.Max(snapshot.State.NextInstanceId,
                rewards.Max(reward => reward.Id) + 1) } };

    private static void CheckDetachedRecipeVariants(PredictorOperationSnapshot original, long currentId, Action<bool, string> check)
    {
        var before = PredictorOperation.Preview(original, ImmutableArray<PredictorInput>.Empty);
        var rows = before.Rewards;
        var first = rows.Single(reward => reward.Id == currentId);
        int count = rows.Length;
        OperationPreview Preview(PredictorOperationSnapshot snapshot) => SurfaceOperationPreview.BuildRewardAlternative(snapshot, currentId, "REROLL", null);
        int Length(PredictorOperationSnapshot snapshot)
        {
            var preview = Preview(snapshot);
            check(preview.Faults.IsEmpty && preview.PendingReasons.IsEmpty, "detached recipe variant settles without a hidden failure");
            if (RerollGroups(preview).Length > 1)
                check(preview.Branches.SelectMany(branch => branch.Sections).Where(section => section.Source.Entry == "DRIFTWOOD")
                    .All(section => section.Style == new OperationDisplayStyle(OperationTextMode.None, OperationCardLayout.Groups)),
                    "detached multi-step reroll uses existing D3 grouped-card presentation");
            return RerollGroups(preview).Length;
        }
        var elite = rows.Select(row => row with { RerollRecipe = row.RerollRecipe! with {
            Odds = PredictorCardOdds.Elite, EncounterSource = true, IsFromCombat = false } }).ToImmutableArray();
        elite = elite.SetItem(elite.Length - 1, elite[^1] with { RerollRecipe = elite[^1].RerollRecipe! with { Odds = PredictorCardOdds.Boss } });
        var whiteStar = DetachedRewardRows(original, elite, currentId);
        check(Length(whiteStar) == count - 1,
            "detached Elite plus White Star keeps the Boss recipe out of the Elite reroll sequence despite equal ordinary draw counts");
        check(Preview(whiteStar).Branches.SelectMany(branch => branch.Sections)
            .Any(section => section.Label == "RewardRerollSameRecipe"),
            "detached mixed recipes explicitly qualify the displayed same-recipe subset");
        var boss = whiteStar with { Scene = new PredictorOperationRewardsScene(elite.Select(row => row with {
            CardSelectionEntered = row.Id == elite[^1].Id }).ToImmutableArray(), elite[^1].Id) };
        check(RerollGroups(SurfaceOperationPreview.BuildRewardAlternative(boss, elite[^1].Id, "REROLL", null)).Length == 1,
            "detached White Star Boss hover does not claim the ordinary Elite rerolls as equivalent");
        var combat = rows.Select(row => row with { RerollRecipe = row.RerollRecipe! with {
            EncounterSource = true, IsFromCombat = row.Id == currentId } }).ToImmutableArray();
        var candyBase = DetachedRewardRows(original, combat, currentId);
        var candy = new PredictorRelic(original.State.NextInstanceId, new("RELIC", "LASTING_CANDY"), original.State.Position.Floor);
        PredictorOperationSnapshot WithCandy(int seen) => candyBase with { State = candyBase.State with {
            NextInstanceId = candyBase.State.NextInstanceId + 1,
            Relics = candyBase.State.Relics.Add(candy with { CombatRewardsSeen = seen }) } };
        check(Length(WithCandy(0)) == count,
            "detached Candy held but currently inactive normalizes IsFromCombat instead of splitting equivalent groups by possession");
        var active = WithCandy(1);
        check(Length(active) == 1 && RerollGroups(Preview(active)).Single().Cards.Length == first.RerollRecipe!.CardCount + 1,
            "detached currently active Candy separates the combat base group and uses recipe count rather than old visible card count");
        var noPowers = original.Context.Catalog!.CharacterCards.Where(card => card.CardType != EffectCardType.Power)
            .Select(card => card.CardKey).ToImmutableArray();
        var noPowerRows = combat.Select(row => row with { RerollRecipe = row.RerollRecipe! with {
            CardPoolOverride = noPowers, NoCardPoolModifications = true } }).ToImmutableArray();
        check(Length(DetachedRewardRows(active, noPowerRows, currentId)) == count,
            "detached active Candy without an available Power has no effective insertion and permits equivalent IsFromCombat recipes");
        var unavailable = rows.Select((row, index) => index switch {
            1 => row with { HasRerolled = true, CanReroll = false },
            2 => row with { Status = PredictorRewardStatus.Taken, CardSelectionEntered = false }, _ => row }).ToImmutableArray();
        check(Length(DetachedRewardRows(original, unavailable, currentId)) == count - 2,
            "detached already rerolled and taken groups do not inflate the remaining sequence");
        var skipped = rows.SetItem(1, rows[1] with { Status = PredictorRewardStatus.Skipped, CardSelectionEntered = false });
        check(Length(DetachedRewardRows(original, skipped, currentId)) == count - 1,
            "detached completed/skipped group is excluded without inventing an RNG reroll for it");
        var longRows = rows.Add(first with { Id = original.State.NextInstanceId + 100, CardSelectionEntered = false })
            .Add(first with { Id = original.State.NextInstanceId + 101, CardSelectionEntered = false });
        check(Length(DetachedRewardRows(original, longRows, currentId)) == count + 2,
            "detached seven-group same-recipe sequence is not truncated to Orrery's five");
    }

    private static void CheckDetachedSacrificeVariants(PredictorOperationSnapshot original, long currentId,
        long wingId, Action<bool, string> check)
    {
        var rows = PredictorOperation.Preview(original, ImmutableArray<PredictorInput>.Empty).Rewards;
        var first = rows.Single(reward => reward.Id == currentId);
        var mixed = rows.Select((row, index) => row with { RerollRecipe = row.RerollRecipe! with {
            Odds = index % 2 == 0 ? PredictorCardOdds.Elite : PredictorCardOdds.Boss,
            EncounterSource = true, IsFromCombat = index == 0 } }).ToImmutableArray();
        foreach (int parity in new[] { 0, 1 })
        {
            var snapshot = DetachedRewardRows(original, mixed, currentId) with { State = original.State with {
                Relics = original.State.Relics.Select(relic => relic.Id == wingId ? relic with { RewardsSacrificed = parity } : relic).ToImmutableArray() } };
            var preview = SurfaceOperationPreview.BuildRewardAlternative(snapshot, currentId, "SACRIFICE", wingId);
            check(preview.Faults.IsEmpty && preview.Notices.Contains("RewardSacrificeProgress:" + parity) &&
                SacrificeRelics(preview).Length == (mixed.Length + parity) / 2,
                "detached mixed Elite/Boss recipes share the sacrifice sequence at original parity " + parity);
        }
        var longRows = rows.Add(first with { Id = original.State.NextInstanceId + 100, CardSelectionEntered = false })
            .Add(first with { Id = original.State.NextInstanceId + 101, CardSelectionEntered = false });
        var longPreview = SurfaceOperationPreview.BuildRewardAlternative(DetachedRewardRows(original, longRows, currentId), currentId, "SACRIFICE", wingId);
        check(longPreview.Faults.IsEmpty && SacrificeRelics(longPreview).Length == 4,
            "detached seven-group sacrifice sequence uses all remaining groups rather than a fixed five-group bound");
    }
}
