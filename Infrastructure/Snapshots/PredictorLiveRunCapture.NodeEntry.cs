using System.Collections.Immutable;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal enum LiveRoomProgress { Unobserved, AwaitingChoice, Resolving, Interrupted, Settled }

internal static partial class PredictorLiveRunCapture
{
    private static readonly System.Reflection.FieldInfo? EventOptionTasks =
        AccessTools.Field(typeof(EventSynchronizer), "_pendingOptionTasks");
    private sealed class TreasureTasks
    {
        internal Task? OpenChest;
        internal readonly List<Task> Obtains = [];
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TreasureRoom, TreasureTasks> ChestTasks = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RestSiteRoom, Task<bool>> RestChoices = new();

    internal static LiveRoomProgress RoomProgress(IRunState run) => RoomProgress(run, out _);

    internal static LiveRoomProgress RoomProgress(IRunState run, out int completedRestChoices)
    {
        completedRestChoices = 0;
        if (run is not RunState native || native.Players.Count != 1)
            return LiveRoomProgress.Unobserved;
        if (native.BaseRoom is RestSiteRoom rest)
        {
            completedRestChoices = native.CurrentMapPointHistoryEntry?.GetEntry(native.Players[0].NetId).RestSiteChoices.Count ?? 0;
            if (RestChoices.TryGetValue(rest, out var action) && !action.IsCompletedSuccessfully)
                return action.IsCompleted ? LiveRoomProgress.Interrupted : LiveRoomProgress.Resolving;
            if (!ReferenceEquals(native.CurrentRoom, rest)) return LiveRoomProgress.Resolving;
            // After a successful action the native Proceed button is available
            // even with Miniature Tent's other camp actions still enabled.
            return completedRestChoices > 0 ? LiveRoomProgress.Settled : LiveRoomProgress.AwaitingChoice;
        }
        if (native.BaseRoom is TreasureRoom chest)
        {
            if (!ChestTasks.TryGetValue(chest, out var tasks) || tasks.OpenChest == null)
                return LiveRoomProgress.AwaitingChoice;
            if (!tasks.OpenChest.IsCompleted || tasks.Obtains.Any(task => !task.IsCompleted) ||
                !ReferenceEquals(native.CurrentRoom, chest)) return LiveRoomProgress.Resolving;
            return tasks.OpenChest.IsCompletedSuccessfully && tasks.Obtains.All(task => task.IsCompletedSuccessfully)
                ? LiveRoomProgress.Settled : LiveRoomProgress.Interrupted;
        }
        if (native.BaseRoom is not EventRoom room) return LiveRoomProgress.Unobserved;
        if (!ReferenceEquals(native.CurrentRoom, room)) return LiveRoomProgress.Resolving;
        var sync = RunManager.Instance.EventSynchronizer;
        if (sync?.Events is [var ev] && ev.Id == room.CanonicalEvent.Id && ReferenceEquals(ev.Owner, native.Players[0]))
        {
            // Several ordinary events set their final page before awaiting a reward
            // or combat. The synchronizer retains every chosen-option task until exit.
            var tasks = EventOptionTasks?.GetValue(sync) as List<Task>;
            if (tasks?.Any(task => !task.IsCompleted) == true) return LiveRoomProgress.Resolving;
            if (tasks?.Any(task => !task.IsCompletedSuccessfully) == true) return LiveRoomProgress.Interrupted;
            if (ev.IsFinished) return tasks != null || room.IsPreFinished ? LiveRoomProgress.Settled : LiveRoomProgress.Resolving;
            return tasks?.Count > 0 || ev.CurrentOptions.Any(option => option.WasChosen)
                ? LiveRoomProgress.Resolving : LiveRoomProgress.AwaitingChoice;
        }
        // Restored prefinished Ancient rooms can precede their local model.
        return room.IsPreFinished ? LiveRoomProgress.Settled : LiveRoomProgress.Unobserved;
    }

    private sealed class NodeEntry(PredictorLiveRunSnapshot snapshot, MapCoord target,
        ImmutableArray<MapCoord> previous)
    {
        internal readonly PredictorLiveRunSnapshot Snapshot = snapshot;
        internal readonly MapCoord Target = target;
        internal readonly ImmutableArray<MapCoord> Previous = previous;
        internal Task? EntryTask;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RunState, NodeEntry> NodeEntries = new();
    // State is a private native property. Its getter only returns the current
    // RunState; the public DebugOnlyGetState API is explicitly reserved for tests.
    private static readonly System.Reflection.PropertyInfo NativeState =
        AccessTools.Property(typeof(RunManager), "State") ?? throw new MissingMemberException(typeof(RunManager).FullName, "State");

    internal static void InstallNodeEntryObserver(Harmony harmony, System.Reflection.MethodInfo? target = null)
    {
        harmony.Patch(target ?? AccessTools.Method(typeof(RunManager), nameof(RunManager.EnterMapCoord), [typeof(MapCoord)])
            ?? throw new MissingMethodException(typeof(RunManager).FullName, "EnterMapCoord(MapCoord)"),
            prefix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(BeforeNativeNodeEntry)),
            postfix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(AfterNativeNodeEntry)));
        harmony.Patch(AccessTools.Method(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption)),
            postfix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(ObserveRestChoice)));
        harmony.Patch(AccessTools.Method(typeof(NTreasureRoom), "OpenChest"),
            postfix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(ObserveOpenedChest)));
        harmony.Patch(AccessTools.Method(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)]),
            postfix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(ObserveChestObtain)));
    }

    private static void ObserveRestChoice(Task<bool> __result)
    {
        if (NativeState.GetValue(RunManager.Instance) is RunState { CurrentRoom: RestSiteRoom room })
            RestChoices.AddOrUpdate(room, __result);
    }

    private static void ObserveOpenedChest(TreasureRoom ____room, Task __result) =>
        ChestTasks.GetOrCreateValue(____room).OpenChest = __result;

    // The chest UI starts each RelicCmd.Obtain independently of the relic
    // picking task. Opening the map is possible while its picker is still open.
    private static void ObserveChestObtain(Player player, Task<RelicModel> __result)
    {
        if (!__result.IsCompletedSuccessfully && player.RunState.CurrentRoom is TreasureRoom room)
            ChestTasks.GetOrCreateValue(room).Obtains.Add(__result);
    }

    private static void BeforeNativeNodeEntry(RunManager __instance, MapCoord coord, out bool __state)
    {
        __state = false;
        if (NativeState.GetValue(__instance) is not RunState run) return;
        try
        {
            if (!(InRunFeaturePreferences.CachedMapPredictionEnabled ?? InRunFeaturePreferences.MapPredictionEnabled))
            {
                RuntimeSnapshotThreadGuard.RequireMainThread();
                // A genuine off-feature entry invalidates the old room's
                // receipt. A duplicate native no-op keeps a still-valid entry
                // available if the feature is enabled again in this same room.
                if (!run.VisitedMapCoords.Contains(coord)) NodeEntries.Remove(run);
                return;
            }
            __state = CaptureBeforeNodeEntry(run, ModRuntime.Snapshot.Detection.NormalizedVersion, coord) != null;
        }
        catch (Exception ex) { RuntimeLog.WarnException("inRunNodeEntryCaptureFailed=true", ex); }
    }

    private static void AfterNativeNodeEntry(RunManager __instance, MapCoord coord, Task __result, bool __state)
    {
        if (__state && NativeState.GetValue(__instance) is RunState run) RecordNodeEntryTask(run, coord, __result);
    }

    internal static void RecordNodeEntryTask(RunState run, MapCoord target, Task entryTask)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(entryTask);
        if (NodeEntries.TryGetValue(run, out var entry) && entry.Target == target) entry.EntryTask = entryTask;
    }

    // EnterMapCoord is synchronous up to AddVisitedMapCoord, so this prefix
    // precedes coordinate/history changes and all room generation RNG. The
    // restore and debug-entry paths do not call it and cannot invent a basis.
    internal static PredictorLiveRunSnapshot? CaptureBeforeNodeEntry(RunState run, string version, MapCoord target,
        PredictorOperationCapture.CaptureTiming? timing = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (run.VisitedMapCoords.Contains(target)) return null; // Native duplicate entry is a no-op.
        NodeEntries.TryGetValue(run, out var previous);
        NodeEntries.Remove(run);
        var basis = previous?.Snapshot.Context.GameVersion == version ? previous.Snapshot : null;
        bool opening = run.CurrentActIndex == 0 && run.TotalFloor == 0 && run.ExtraFields.StartedWithNeow &&
            run.Map.GetPoint(target) is { PointType: MegaCrit.Sts2.Core.Map.MapPointType.Ancient };
        var snapshot = Capture(run, version, true, basis, timing, captureOpeningOptions: opening) with
        { EntryPoint = Position(target), EntryActIndex = run.CurrentActIndex };
        NodeEntries.Add(run, new(snapshot, target, run.VisitedMapCoords.ToImmutableArray()));
        return snapshot;
    }

    internal static PredictorLiveRunSnapshot? CurrentNodeEntry(RunState run, string version) =>
        CurrentNodeEntry(run, version, RoomProgress(run));

    internal static PredictorLiveRunSnapshot? CurrentNodeEntry(RunState run, string version, LiveRoomProgress roomProgress)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!NodeEntries.TryGetValue(run, out var entry) ||
            entry.Snapshot.Context.GameVersion != version || entry.Snapshot.EntryActIndex != run.CurrentActIndex ||
            entry.EntryTask is not { IsCompletedSuccessfully: true } || run.CurrentMapCoord != entry.Target ||
            run.TotalFloor != entry.Snapshot.State.Position.Floor + 1 ||
            run.VisitedMapCoords.Count != entry.Previous.Length + 1 ||
            !run.VisitedMapCoords.Take(entry.Previous.Length).SequenceEqual(entry.Previous)) return null;
        if (roomProgress is LiveRoomProgress.Resolving or LiveRoomProgress.Interrupted) return null;
        if (roomProgress == LiveRoomProgress.Settled)
        {
            NodeEntries.Remove(run);
            return null;
        }
        return entry.Snapshot;
    }
}
