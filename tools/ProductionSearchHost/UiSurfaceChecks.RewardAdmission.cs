using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Reuse the existing native rewards overlay and synchronizer. These are
    // controlled already populated offers; no encounter/reward RNG generation,
    // pickup or combat is executed to establish the admission boundary.
    private static async Task RunRewardAdmissionChecks(Node root, string output, SubViewport viewport,
        Control hoverRoot, RunState run, Player player, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check)
    {
        string receipt = Path.Combine(output, "reward-admission-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var evidence = new List<object>();
        string original = signature();
        var originalRoom = run.CurrentRoom;
        var relicHistory = run.CurrentMapPointHistoryEntry?.GetEntry(player.NetId).RelicChoices;
        var originalRelicHistory = relicHistory?.ToArray();
        var room = new CombatRoom(ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Monster).ToMutable(), run);
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Move(Vector2 point)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frames();
        }
        void LeaveController()
        {
            if (Controller() is { } controller && PredictorOperationCapture.ReadNullable(controller, "_owner") is Control owner)
                NeowOperationHoverController.Leave(owner);
        }
        check(overlays.Peek() == null && PredictorOperationCapture.NativeRewardSets(run, player).IsEmpty,
            "reward admission fixture starts without an overlay or suspended rewards stack");
        run.PushRoom(room);
        try
        {
            foreach (var scenario in new (string Name, RelicModel Relic, bool Bound, bool Linked, bool Nested, bool Expected)[] {
                ("terminal-passive", ModelDb.Relic<Anchor>(), true, false, false, false),
                ("terminal-public-override", ModelDb.Relic<Strawberry>(), true, false, false, false),
                ("terminal-random-obtain", ModelDb.Relic<Whetstone>(), true, false, false, true),
                ("unbound-custom-passive", ModelDb.Relic<Anchor>(), false, false, false, true),
                ("linked-passive", ModelDb.Relic<Anchor>(), true, true, false, true),
                ("nested-stack-passive", ModelDb.Relic<Anchor>(), true, false, true, true) })
            {
                NRewardsScreen? screen = null;
                RewardsSet? set = null;
                RewardsSet? parent = null;
                bool setStarted = false, parentStarted = false;
                try
                {
                    await Move(new(4, viewport.Size.Y - 4)); LeaveController();
                    var reward = new RelicReward(scenario.Relic.ToMutable(), player);
                    set = new RewardsSet(player);
                    if (scenario.Bound) set.EmptyForRoom(room);
                    set.WithCustomRewards(scenario.Linked ? [new LinkedRewardSet([reward], player)] : [reward]);
                    if (scenario.Nested)
                    {
                        parent = new RewardsSet(player).WithCustomRewards([new RelicReward(ModelDb.Relic<Anchor>().ToMutable(), player)]);
                        _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(parent); parentStarted = true;
                    }
                    _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set); setStarted = true;
                    screen = NRewardsScreen.ShowScreen(set, scenario.Bound, run);
                    await Frames(40);
                    var button = Descendants(screen).OfType<NRewardButton>().Single(candidate => ReferenceEquals(candidate.Reward, reward));
                    check(button.IsEnabled && button.IsVisibleInTree() && ReferenceEquals(overlays.Peek(), screen),
                        scenario.Name + " mounts its original enabled native reward button");
                    string before = signature();
                    bool admitted = OperationSurfaceHoverCapture.TryReward(button, out var actual, out long rewardId, out var source);
                    check(admitted == scenario.Expected && (!admitted || ReferenceEquals(actual, run) &&
                        rewardId == PredictorLiveRunCapture.Id(reward) && source.Entry == scenario.Relic.Id.Entry),
                        scenario.Name + " retains the correct direct/linked/nested native reward admission");
                    if (!admitted)
                    {
                        await Move(button.GetGlobalRect().GetCenter());
                        check(Controller() == null || PredictorOperationCapture.ReadNullable(Controller()!, "_owner") == null &&
                            PredictorOperationCapture.ReadNullable(Controller()!, "_work") == null &&
                            Controller()!.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay") == null,
                            scenario.Name + " original native hover starts no capture, worker or prediction view");
                        await Move(new(4, viewport.Size.Y - 4));
                    }
                    check(signature() == before && !reward.SuccessfullySelected,
                        scenario.Name + " admission preserves player state, RNG and reward availability");
                    evidence.Add(new { scenario.Name, item = scenario.Relic.Id.Entry, scenario.Bound,
                        scenario.Linked, scenario.Nested, admitted, nativeButton = button.GetType().Name });
                }
                finally
                {
                    LeaveController();
                    if (screen != null && GodotObject.IsInstanceValid(screen)) overlays.Remove(screen);
                    if (setStarted) RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                    if (parentStarted) RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
                    await Frames();
                }
            }
        }
        finally
        {
            LeaveController();
            check(ReferenceEquals(run.CurrentRoom, room), "reward admission fixture retains its controlled current combat room");
            run.PopCurrentRoom();
            if (relicHistory != null && originalRelicHistory != null)
            { relicHistory.Clear(); relicHistory.AddRange(originalRelicHistory); }
        }
        check(ReferenceEquals(run.CurrentRoom, originalRoom) && signature() == original &&
            PredictorOperationCapture.NativeRewardSets(run, player).IsEmpty && overlays.Peek() == null,
            "reward admission fixture restores room, native player/RNG state, rewards stack and overlay");
        File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, nativeAdmission = true,
            actualObtainParentReplay = false, ownerRuntimeAcceptance = false, evidence }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
