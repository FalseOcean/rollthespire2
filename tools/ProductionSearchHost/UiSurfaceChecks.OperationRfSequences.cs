using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
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
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunRfRerollSequenceChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        NEventRoom eventNode, NGlobalUi global, Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Harmony fixture, Action<object, string, object?> set)
    {
        string original = signature();
        var originalRooms = PredictorOperationCapture.Read<List<AbstractRoom>>(run, "_currentRooms").ToArray();
        var originalEvent = PredictorOperationCapture.ReadNullable(eventNode, "_event");
        var eventSync = RunManager.Instance.EventSynchronizer;
        var syncEvents = PredictorOperationCapture.Read<List<EventModel>>(eventSync, "_events");
        var oldSyncEvents = syncEvents.ToArray();
        var oldCanonicalEvent = PredictorOperationCapture.ReadNullable(eventSync, "_canonicalEvent");
        uint oldPage = PredictorOperationCapture.Read<uint>(eventSync, "_pageIndex");
        var syncVotes = PredictorOperationCapture.Read<List<uint?>>(eventSync, "_playerVotes");
        var oldVotes = syncVotes.ToArray();
        var originalOverlays = global.Overlays;
        var oldRelics = player.Relics.ToArray();
        var oldPersonalBag = player.RelicGrabBag.ToSerializable(); var oldSharedBag = run.SharedRelicGrabBag.ToSerializable();
        var oldCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
        var oldDiscovered = player.DiscoveredCards.ToArray();
        var oldDiscoveredRelics = player.DiscoveredRelics.ToArray();
        var oldPlayerRng = player.PlayerRng.ToSerializable(); var oldOdds = player.PlayerOdds.ToSerializable();
        var oldRng = run.Rng.ToSerializable();
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var oldHistory = history.Select(act => act.ToArray()).ToArray();
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false);
        var originalModes = preferences.Preferences.OperationPredictionModes;
        bool originalEnabled = preferences.Preferences.OperationPredictionEnabled;
        var evidence = new List<object>();
        RelicModel? addedDriftwood = null;
        var overlays = new NOverlayStack(); _neowStaticSceneNodes.Add(overlays);
        fixture.Patch(AccessTools.Method(typeof(NOverlayStack), "_Ready"),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(NeowSkipStaticSceneStartup)));
        var backstop = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlays.AddChild(backstop); set(overlays, "_backstop", backstop);
        set(global, nameof(NGlobalUi.Overlays), overlays); game.AddChild(overlays);
        overlays.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); backstop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        RewardsSet? rewards = null; NCardRewardSelectionScreen? picker = null; Task<bool>? selecting = null;
        bool passed = false; string? failure = null;
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!ready() && watch.Elapsed.TotalSeconds < 15) await Frames(1); check(ready(), reason); }
        NeowOperationHoverController Controller() => hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        OperationPreview? Preview() => Controller().GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay") is { Visible: true } view
            ? PredictorOperationCapture.ReadNullable(view, "_preview") as OperationPreview : null;
        async Task Suppressed(string name, Control owner, string extra)
        {
            preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.Auto);
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.Auto);
            NeowOperationHoverController.Leave(owner);
            viewport.PushInput(new InputEventMouseMotion { Position = owner.GetGlobalRect().GetCenter(), GlobalPosition = owner.GetGlobalRect().GetCenter() }, true);
            NeowOperationHoverController.Focus(owner, runtime.Detection.NormalizedVersion); await Frames(6);
            var controller = Controller();
            check(PredictorOperationCapture.ReadNullable(controller, "_snapshot") == null && PredictorOperationCapture.ReadNullable(controller, "_work") == null &&
                Preview() == null && PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(controller, "_timing").CaptureMs == 0,
                name + " Auto defers every loaded RF sequence before native capture and worker startup");
            var captureDemand = typeof(NeowOperationHoverController).GetMethod("CaptureDemand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, [typeof(RandomForeseerSnapshot)], null)!;
            foreach (var rf in new[] { new RandomForeseerSnapshot(RandomForeseerStatus.Known, "0.13.20", "Loaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.Known, "0.13.19", "Loaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.Known, "0.8.4", "Loaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.Known, "", "Loaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.Known, "unrecognized-version", "Loaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.NotLoaded, "", "NotLoaded"),
                         new RandomForeseerSnapshot(RandomForeseerStatus.Unknown, "0.13.20", "LoadDetectionFailed") })
            foreach (var mode in Enum.GetValues<OperationPredictionMode>())
            {
                preferences.SetOperationPredictionMode(extra, mode);
                var demand = (OperationDemand)captureDemand.Invoke(controller, [rf])!;
                bool expectedSequence = mode == OperationPredictionMode.On || mode == OperationPredictionMode.Auto && !rf.IsLoaded;
                check(demand.Surface.IncludeRewardSequence == expectedSequence &&
                    demand.Surface.IncludeRewardAlternative == !rf.IsLoaded &&
                    demand.MayPredict == (expectedSequence || !rf.IsLoaded),
                    name + ":" + rf.Status + ":" + rf.Version + ":" + mode +
                    " admits sequence only for forced On or Auto without confirmed RF, independently of the immediate Auto result");
                preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.Off);
                var sequenceOnly = (OperationDemand)captureDemand.Invoke(controller, [rf])!;
                check(sequenceOnly.Surface.IncludeRewardSequence == expectedSequence &&
                    !sequenceOnly.Surface.IncludeRewardAlternative && sequenceOnly.MayPredict == expectedSequence,
                    name + " sequence tri-state remains independent when the immediate result is Off");
                evidence.Add(new { name, detectionProbe = true, status = rf.Status.ToString(), rf.Version, mode = mode.ToString(),
                    sequence = demand.Surface.IncludeRewardSequence, immediate = demand.Surface.IncludeRewardAlternative,
                    mayPredict = demand.MayPredict, sequenceOnly = sequenceOnly.MayPredict });
                preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.Auto);
            }
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.Auto);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-" + name + "-auto-zh.png"));
            evidence.Add(new { name, mode = "Auto", capture = false, worker = false, displayed = false });
        }
        async Task TriState(string name, Control owner, Control hitbox, string source,
            Func<PredictorOperationScene, bool>? scene, Func<OperationPreview, bool> outcome,
            Action<NeowOperationOverlay, OperationPreview>? inspect, string extra)
        {
            await Suppressed(name, owner, extra);
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.On);
            await exercise(name + "-on", owner, hitbox, source, scene, outcome, inspect, true);
            evidence.Add(new { name, mode = "On", groups = RerollGroups(Preview()!).Length });
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.Off);
            await Until(() => Preview() == null, name + " supplement Off clears the same hover");
            await Frames();
            check(PredictorOperationCapture.ReadNullable(Controller(), "_snapshot") == null && PredictorOperationCapture.ReadNullable(Controller(), "_work") == null,
                name + " Off stops the detached work independently of the suppressed base");
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.Auto);
            preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.On);
            await Until(() => Preview() is { Faults.IsEmpty: true } direct && RerollGroups(direct).Length == 1,
                name + " explicit base On with sequence Auto retains exactly its direct result");
            preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.Off);
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.On);
            await Until(() => Preview() is { Faults.IsEmpty: true } full && outcome(full), name + " independently forced supplement retains the full sequence with base Off");
            NeowOperationHoverController.Leave(owner);
            preferences.SetOperationPredictionMode("reward:reroll", OperationPredictionMode.Auto);
            preferences.SetOperationPredictionMode(extra, OperationPredictionMode.Auto);
        }
        async Task CheckNativeSet(string name, RewardsSet nativeSet, bool checkTwoRemaining)
        {
            rewards = nativeSet; await rewards.GenerateWithoutOffering();
            var cards = rewards.Rewards.OfType<CardReward>().ToArray();
            check(cards.Length == (name.Contains("mixed", StringComparison.Ordinal) ? 4 : checkTwoRemaining ? 3 : 6), name + " uses its actual native card reward count");
            _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewards);
            selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(cards[0]);
            await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted, name + " opens its native picker");
            if (selecting.IsFaulted) throw new InvalidOperationException("Native RF sequence picker failed", selecting.Exception);
            check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen, name + " awaits original player input");
            picker = (NCardRewardSelectionScreen)overlays.Peek()!; await Frames(30);
            var options = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(picker, "_extraOptions");
            int index = options.ToList().FindIndex(option => option.OptionId == "REROLL");
            var button = Descendants(picker).OfType<NCardRewardAlternativeButton>().ElementAt(index);
            var captured = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var full = SurfaceOperationPreview.BuildRewardAlternative(captured, PredictorLiveRunCapture.Id(cards[0]), "REROLL", null);
            var fullGroups = RerollGroups(full);
            check(full.Faults.IsEmpty && fullGroups.Length > 1, name + " independent complete model supplies the forced-On reference");
            await TriState(name, button, button, "DRIFTWOOD", scene => scene is PredictorOperationRewardsScene,
                preview => RerollGroups(preview).Length == fullGroups.Length && RerollGroups(preview).Zip(fullGroups).All(pair => SameCards(pair.First.Cards, pair.Second.Cards)),
                (view, preview) => CheckOperationIconPaths(view, preview, check), "extra:reroll-sequence");
            if (checkTwoRemaining)
            {
                set(cards[2], "_hasBeenRerolled", true);
                await Suppressed("rf-two-remaining-sequence", button, "extra:reroll-sequence");
                NeowOperationHoverController.Leave(button); set(cards[2], "_hasBeenRerolled", false);
            }
            int skip = options.ToList().FindIndex(option => option.OptionId.Equals("Skip", StringComparison.OrdinalIgnoreCase));
            AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(picker, [skip]);
            await Until(() => selecting.IsCompleted, name + " exits through original Skip");
            picker = null; selecting = null; RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); rewards = null;
        }
        try
        {
            check(RandomForeseerCompatibility.Capture().IsLoaded, "actual RF package is confirmed loaded without a version gate");
            preferences.SetOperationPredictionEnabled(true);
            if (!player.Relics.OfType<Driftwood>().Any())
            { addedDriftwood = ModelDb.Relic<Driftwood>().ToMutable(); player.AddRelicInternal(addedDriftwood, silent: true); }
            while (run.CurrentRoom != null) run.PopCurrentRoom();
            set(eventNode, "_event", null);
            run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
            SaveManager.Instance.MarkFtueAsComplete("power_card_ftue");
            await CheckNativeSet("rf-same-recipe-sequence", new RewardsSet(player).WithCustomRewards(
                Enumerable.Range(0, 3).Select(_ => (Reward)new CardReward(CardCreationOptions.ForRoom(player, RoomType.Monster), 3, player)).ToList()), true);
            await CheckNativeSet("rf-six-group-sequence", new RewardsSet(player).WithCustomRewards(
                Enumerable.Range(0, 6).Select(_ => (Reward)new CardReward(CardCreationOptions.ForRoom(player, RoomType.Monster), 3, player)).ToList()), false);
            var candy = (LastingCandy)ModelDb.Relic<LastingCandy>().ToMutable(); candy.CombatRewardsSeen = 1;
            var star = ModelDb.Relic<WhiteStar>().ToMutable();
            player.AddRelicInternal(candy, silent: true); player.AddRelicInternal(star, silent: true);
            var combat = new CombatRoom(ModelDb.AllEncounters.First(encounter => encounter.RoomType == RoomType.Elite).ToMutable(), run);
            run.PushRoom(combat); run.AppendToMapPointHistory(MapPointType.Elite, RoomType.Elite, combat.Encounter.Id);
            for (int hunt = 0; hunt < 2; hunt++) combat.AddExtraReward(player,
                new CardReward(CardCreationOptions.ForRoom(player, RoomType.Elite), 3, player));
            await CheckNativeSet("rf-candy-white-star-mixed-sequence", new RewardsSet(player).WithRewardsFromRoom(combat), false);
            run.PopCurrentRoom(); player.RemoveRelicInternal(candy, silent: true); player.RemoveRelicInternal(star, silent: true);
            await RunPhilosopherRerollRecipeChecks(root, output, runtime, game, run, player, eventNode, overlays,
                signature, check, (name, owner, hitbox, source, scene, outcome, inspect, _) =>
                    TriState("rf-" + name, owner, hitbox, source, scene, outcome, inspect, "extra:reroll-sequence"), set);
            passed = true;
        }
        catch (Exception error)
        {
            failure = error.ToString();
            File.WriteAllText(Path.Combine(output, "operation-rf-sequences-primary-failure.json"), JsonSerializer.Serialize(new { failure, evidence }));
            throw;
        }
        finally
        {
            if (picker != null && selecting is { IsCompleted: false })
            { PredictorOperationCapture.Read<TaskCompletionSource<int?>>(picker, "_completionSource").TrySetResult(null); await Until(() => selecting.IsCompleted, "RF sequence fixture unwinds picker"); }
            try { if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards)) RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); }
            catch (Exception cleanup) { failure = (failure == null ? "" : failure + "\n") + "Cleanup: " + cleanup; passed = false; }
            foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !oldCards.Contains(card)).ToArray()) run.RemoveCard(card);
            foreach (var relic in player.Relics.Where(relic => !oldRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
            set(player, nameof(Player.DiscoveredCards), oldDiscovered.ToList()); set(player, nameof(Player.DiscoveredRelics), oldDiscoveredRelics.ToList());
            player.RelicGrabBag.LoadFromSerializable(oldPersonalBag); run.SharedRelicGrabBag.LoadFromSerializable(oldSharedBag);
            player.PlayerRng.LoadFromSerializable(oldPlayerRng); player.PlayerOdds.LoadFromSerializable(oldOdds); run.Rng.LoadFromSerializable(oldRng);
            set(eventNode, "_event", originalEvent);
            syncEvents.Clear(); syncEvents.AddRange(oldSyncEvents); set(eventSync, "_canonicalEvent", oldCanonicalEvent); set(eventSync, "_pageIndex", oldPage);
            syncVotes.Clear(); syncVotes.AddRange(oldVotes);
            while (run.CurrentRoom != null) run.PopCurrentRoom();
            foreach (var room in originalRooms) run.PushRoom(room);
            history.Clear(); foreach (var act in oldHistory) history.Add(act.ToList());
            set(global, nameof(NGlobalUi.Overlays), originalOverlays); overlays.Free();
            foreach (var entry in OperationPredictionCatalog.Entries) preferences.SetOperationPredictionMode(entry.Id, originalModes.Get(entry.Id));
            preferences.SetOperationPredictionEnabled(originalEnabled);
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, "operation-rf-sequences-restore-before.json"), original);
                File.WriteAllText(Path.Combine(output, "operation-rf-sequences-restore-after.json"), signature());
            }
            File.WriteAllText(Path.Combine(output, "operation-rf-sequences-result.json"), JsonSerializer.Serialize(new { passed = passed && restored, restored, failure, evidence,
                actualRf = RandomForeseerCompatibility.Capture(),
                scope = "Actual initialized RF/RitsuLib copies; native same-recipe three/six-group, Elite base/two Hunt/White Star/active Candy and ColorfulPhilosophers reward selection; independent source/supplement tri-state and no-capture Auto; no natural-run RF artwork claim" }, new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "RF sequence fixture restores complete original native state");
        }
    }
}
