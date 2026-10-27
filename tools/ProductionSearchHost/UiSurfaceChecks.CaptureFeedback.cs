using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunPrecariousShearsOperationChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, NEventOptionButton button, Control hoverRoot,
        Func<string> signature, Action<bool, string> check)
    {
        check(button.Option.Relic is PrecariousShears, "performance fixture uses the original Neow shears offer");
        string before = signature();
        var samples = new List<object>();
        for (int iteration = 0; iteration < 4; iteration++)
        {
            var timing = new PredictorOperationCapture.CaptureTiming();
            var watch = Stopwatch.StartNew();
            var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game, out _, timing);
            double captureMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var preview = await Task.Run(() => RelicOperationPreview.Build(snapshot, PredictorLiveRunCapture.Id(button.Option)));
            double workerMs = watch.Elapsed.TotalMilliseconds;
            check(preview.Faults.IsEmpty && !NeowOperationOverlay.HasVisibleContent(preview),
                "original shears prediction has no useful random pickup result");
            samples.Add(new { iteration, captureMs, workerMs, timing.NativeStampMs, timing.CatalogMs,
                timing.CompleteCatalogMs, timing.HashMs, timing.AllocatedBytes,
                paths = preview.Branches.Sum(branch => branch.PathsExamined), preview.PendingReasons });
        }
        NeowOperationHoverController.Focus(button, runtime.Detection.NormalizedVersion);
        await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        var active = hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        check(active == null || PredictorOperationCapture.ReadNullable(active, "_owner") == null,
            "native shears offer is excluded before main-thread capture and worker start");
        check(signature() == before, "shears measurements and excluded hover preserve the native run and cursors");
        File.WriteAllText(Path.Combine(output, "precarious-shears-operation-performance.json"), JsonSerializer.Serialize(new
        { passed = true, samples, nativeEntry = "Neow PRECARIOUS_SHEARS", randomPickupResult = false,
            admissionExcluded = true, ownerObservedStallReproduced = false }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task RunCrystalLootOperationChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, Player player, NEventRoom eventNode, NGlobalUi global, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        // Execute the native one-off loot producer with original item objects.
        // The revealed set is controlled, while ToReward/OfferCustom/pickers,
        // event RNG override, rerolls and sacrifices all remain native.
        string original = signature();
        var oldEvent = PredictorOperationCapture.ReadNullable(eventNode, "_event");
        var oldRelics = player.Relics.ToArray();
        var relics = PredictorOperationCapture.Read<List<RelicModel>>(player, "_relics");
        var oldCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
        var oldDiscoveredCards = player.DiscoveredCards.ToArray();
        var oldDiscoveredRelics = player.DiscoveredRelics.ToArray();
        var oldPlayerRng = player.PlayerRng.ToSerializable();
        var oldRunRng = run.Rng.ToSerializable();
        var oldOdds = player.PlayerOdds.ToSerializable();
        var oldPersonal = player.RelicGrabBag.ToSerializable();
        var oldShared = run.SharedRelicGrabBag.ToSerializable();
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var oldHistory = history.Select(act => act.ToArray()).ToArray();
        var oldInventory = global.RelicInventory;
        var inventory = new NRelicInventory();
        var visuals = new Harmony("rt2.crystal.loot.native.visuals");
        var evidence = new List<object>();
        async Task Frames(int count = 3)
        { for (int frame = 0; frame < count; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        try
        {
            set(global, nameof(NGlobalUi.RelicInventory), inventory);
            visuals.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            foreach (bool sacrifice in new[] { false, true })
            {
                var omitted = relics.Select((relic, index) => (Relic: relic, Index: index))
                    .Where(item => sacrifice && item.Relic is Driftwood).ToArray();
                foreach (var item in omitted) relics.Remove(item.Relic);
                PaelsWing? wing = null;
                if (sacrifice)
                {
                    wing = (PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable(); wing.RewardsSacrificed = 1;
                    player.AddRelicInternal(wing, silent: true);
                }
                else
                {
                    // Both native pool extenders verify that the frozen rarity
                    // predicate survives additions without duplicating pools.
                    player.AddRelicInternal(ModelDb.Relic<DingyRug>().ToMutable(), silent: true);
                    player.AddRelicInternal(ModelDb.Relic<PrismaticGem>().ToMutable(), silent: true);
                }
                var room = new EventRoom(ModelDb.Event<CrystalSphere>()); run.PushRoom(room);
                run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<CrystalSphere>().Id);
                RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<CrystalSphere>());
                var ev = RunManager.Instance.EventSynchronizer.GetLocalEvent();
                await Until(() => ev.CurrentOptions.Count > 0, "original Crystal event initializes");
                set(eventNode, "_event", ev);
                List<CrystalSphereItem> revealed = [new CrystalSphereCardReward(CardRarity.Common, player),
                    new CrystalSphereCardReward(CardRarity.Uncommon, player), new CrystalSphereCardReward(CardRarity.Rare, player)];
                var offering = RunManager.Instance.OneOffSynchronizer.DoLocalCrystalSphereRewards(player, ev.Rng, revealed);
                await Until(() => overlays.Peek() is NRewardsScreen || offering.IsCompleted, "native Crystal loot opens reward screen");
                if (offering.IsFaulted) throw new InvalidOperationException("Native Crystal loot failed", offering.Exception);
                var screen = (NRewardsScreen)overlays.Peek()!;
                var setRewards = PredictorOperationCapture.Read<RewardsSet>(screen, "_rewardsSet");
                var groups = setRewards.Rewards.OfType<CardReward>().ToArray();
                check(groups.Length == 3 && ReferenceEquals(run.CurrentRoom, room), "Crystal loot retains its real event room");
                var inputs = ImmutableArray<PredictorInput>.Empty;
                PredictorOperationSnapshot? initial = null;
                for (int index = 0; index < groups.Length; index++)
                {
                    var reward = groups[index]; long id = PredictorLiveRunCapture.Id(reward);
                    var selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
                    await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted,
                        "Crystal native card picker opens");
                    var picker = (NCardRewardSelectionScreen)overlays.Peek()!; await Frames(12);
                    var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    check(snapshot.Scene is PredictorOperationRewardsScene actualRewards && (sacrifice || actualRewards.EventRng != null),
                        "Crystal native loot captures ordinary reward scene with actual event RNG");
                    var captured = ((PredictorOperationRewardsScene)snapshot.Scene).Rewards.Single(row => row.Id == id);
                    check(captured.RerollRecipe is { UseEventRng: true, NoCardPoolModifications: false, NoUpgradeRoll: false } &&
                        captured.RerollRecipe.Rarity?.ToString() == new[] { "Common", "Uncommon", "Rare" }[index],
                        "Crystal retains fixed-rarity native recipe, flags and RNG override");
                    initial ??= snapshot;
                    if (index > 0) inputs = inputs.Add(new EnterCardSelection(id));
                    string option = sacrifice ? "SACRIFICE" : "REROLL";
                    var alternatives = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(picker, "_extraOptions");
                    int slot = alternatives.ToList().FindIndex(alternative => alternative.OptionId == option);
                    check(slot >= 0, "native Crystal picker exposes " + option);
                    var button = Descendants(picker).OfType<NCardRewardAlternativeButton>().ElementAt(slot);
                    if (index == 0)
                        await exercise("crystal-loot-" + option.ToLowerInvariant(), button, button,
                            sacrifice ? "PAELS_WING" : "DRIFTWOOD", scene => scene is PredictorOperationRewardsScene,
                            preview => preview.Faults.IsEmpty && (sacrifice
                                ? preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                                    .Any(row => row.Action == OperationPreviewAction.ObtainRelic)
                                : preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.CardGroups).Count() == 1),
                            null, false);
                    inputs = inputs.Add(sacrifice ? new SacrificeCardReward(id, PredictorLiveRunCapture.Id(wing!)) : new RerollCardReward(id));
                    string before = signature();
                    var expected = PredictorOperation.Preview(initial, inputs);
                    check(expected.Disposition == PredictorInputResult.Accepted && signature() == before,
                        "detached Crystal alternative preserves the native run");
                    AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(picker, [slot]);
                    if (sacrifice)
                    {
                        await Until(() => selecting.IsCompleted, "Crystal native sacrifice settles");
                        check(selecting.IsCompletedSuccessfully && wing!.RewardsSacrificed == index + 2 &&
                            player.Relics.Select(PredictorLiveRunCapture.Key).SequenceEqual(expected.State.Relics.Select(relic => relic.Key)),
                            "Crystal current and consecutive sacrifices match exact obtained relics");
                    }
                    else
                    {
                        await Until(() => !reward.CanReroll, "Crystal native reroll settles"); await Frames(4);
                        var actual = reward.Cards.Select(card => PredictorLiveRunCapture.Card(card, snapshot.Context.Catalog!)).ToArray();
                        var predicted = expected.Rewards.Single(row => row.Id == id).Cards;
                        check(actual.Select(card => card with { Id = 0, FloorAdded = 0 }).SequenceEqual(
                            predicted.Select(card => card with { Id = 0, FloorAdded = 0 })),
                            "Crystal native identities, upgrades and enchantments match the exact reroll");
                        var after = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                        var eventCursor = ((PredictorOperationRewardsScene)initial.Scene).EventRng!;
                        var eventRng = RolltheSpire2.Core.Rng.Xoshiro256StarStar.FromState(eventCursor.S0, eventCursor.S1, eventCursor.S2, eventCursor.S3, eventCursor.Calls);
                        for (int draw = 0; draw < (index + 1) * 6; draw++) eventRng.NextFloat();
                        var expectedEvent = PredictorStreamState.Capture(PredictorStream.Rewards, eventRng);
                        check(after.State.Streams.SequenceEqual(expected.State.Streams) && after.State.Odds == expected.State.Odds &&
                            CrystalSphereLiveCapture.RngState(PredictorStream.Rewards, ev.Rng) == expectedEvent,
                            "Crystal reroll preserves native personal/shared RNG and exact event cursor");
                        int skip = alternatives.ToList().FindIndex(alternative => alternative.OptionId.Equals("Skip", StringComparison.OrdinalIgnoreCase));
                        AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(picker, [skip]);
                        await Until(() => selecting.IsCompleted, "Crystal native reroll picker exits");
                        inputs = inputs.Add(new ExitCardSelection());
                    }
                    evidence.Add(new { option, ordinal = index + 1, fixedRarity = captured.RerollRecipe!.Rarity.ToString(),
                        rewardsStream = expected.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Calls });
                }
                if (!RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(setRewards))
                    RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                if (GodotObject.IsInstanceValid(screen) && screen.GetParent() != null) overlays.Remove(screen);
                await Until(() => offering.IsCompleted, "original Crystal loot callback completes");
                check(offering.IsCompletedSuccessfully, "Crystal native loot completion succeeds");
                if (sacrifice) await Frames(65);
                ev.EnsureCleanup(); run.PopCurrentRoom();
                foreach (var relic in player.Relics.Where(relic => !oldRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
                foreach (var item in omitted) relics.Insert(item.Index, item.Relic);
                player.PlayerRng.LoadFromSerializable(oldPlayerRng); player.PlayerOdds.LoadFromSerializable(oldOdds);
                player.RelicGrabBag.LoadFromSerializable(oldPersonal); run.SharedRelicGrabBag.LoadFromSerializable(oldShared);
            }
        }
        finally
        {
            visuals.UnpatchAll(visuals.Id); set(global, nameof(NGlobalUi.RelicInventory), oldInventory); inventory.Free();
            foreach (var relic in player.Relics.Where(relic => !oldRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
            relics.Clear(); relics.AddRange(oldRelics);
            foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !oldCards.Contains(card)).ToArray()) run.RemoveCard(card);
            player.PlayerRng.LoadFromSerializable(oldPlayerRng); run.Rng.LoadFromSerializable(oldRunRng); player.PlayerOdds.LoadFromSerializable(oldOdds);
            player.RelicGrabBag.LoadFromSerializable(oldPersonal); run.SharedRelicGrabBag.LoadFromSerializable(oldShared);
            set(player, nameof(Player.DiscoveredCards), oldDiscoveredCards.ToList());
            set(player, nameof(Player.DiscoveredRelics), oldDiscoveredRelics.ToList()); set(eventNode, "_event", oldEvent);
            history.Clear(); foreach (var act in oldHistory) history.Add(act.ToList());
        }
        check(signature() == original, "Crystal native loot fixture restores original run and RNG");
        File.WriteAllText(Path.Combine(output, "crystal-loot-operation-result.json"), JsonSerializer.Serialize(new
        { passed = true, evidence, nativeOneOffLootProducer = true, nativeHoverCaptureWorkerRenderer = true,
            solverPremisesChanged = false, ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
