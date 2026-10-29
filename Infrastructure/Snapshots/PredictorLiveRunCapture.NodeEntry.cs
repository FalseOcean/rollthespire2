using System.Collections.Immutable;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorLiveRunCapture
{
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

    internal static void InstallNodeEntryObserver(Harmony harmony, System.Reflection.MethodInfo? target = null) => harmony.Patch(
        target ?? AccessTools.Method(typeof(RunManager), nameof(RunManager.EnterMapCoord), [typeof(MapCoord)])
            ?? throw new MissingMethodException(typeof(RunManager).FullName, "EnterMapCoord(MapCoord)"),
        prefix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(BeforeNativeNodeEntry)),
        postfix: new HarmonyMethod(typeof(PredictorLiveRunCapture), nameof(AfterNativeNodeEntry)));

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
        var basis = previous?.Snapshot.Run.Context.GameVersion == version ? previous.Snapshot : null;
        bool opening = run.CurrentActIndex == 0 && run.TotalFloor == 0 && run.ExtraFields.StartedWithNeow &&
            run.Map.GetPoint(target) is { PointType: MegaCrit.Sts2.Core.Map.MapPointType.Ancient };
        var snapshot = Capture(run, version, true, basis, timing, captureOpeningOptions: opening) with
        { EntryPoint = Position(target), EntryActIndex = run.CurrentActIndex };
        NodeEntries.Add(run, new(snapshot, target, run.VisitedMapCoords.ToImmutableArray()));
        return snapshot;
    }

    internal static PredictorLiveRunSnapshot? CurrentNodeEntry(RunState run, string version)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!NodeEntries.TryGetValue(run, out var entry) ||
            entry.Snapshot.Run.Context.GameVersion != version || entry.Snapshot.EntryActIndex != run.CurrentActIndex ||
            entry.EntryTask is not { IsCompletedSuccessfully: true } || run.CurrentMapCoord != entry.Target ||
            run.TotalFloor != entry.Snapshot.Run.Working.Position.Floor + 1 ||
            run.VisitedMapCoords.Count != entry.Previous.Length + 1 ||
            !run.VisitedMapCoords.Take(entry.Previous.Length).SequenceEqual(entry.Previous)) return null;
        // Native Ancient completion means all option tasks have settled. Neow's
        // actual outcome now owns the future; its before-entry premise must not
        // replay an opening selection after the player has already completed it.
        // Shop/reward travel availability does not provide this completion fact.
        if (entry.Snapshot.Run.Working is { StartedWithNeow: true, OpeningCompleted: false } &&
            run.CurrentActIndex == 0 && run.BaseRoom is EventRoom
            { IsPreFinished: true, CanonicalEvent.Id.Entry: "NEOW" })
        {
            NodeEntries.Remove(run);
            return null;
        }
        return entry.Snapshot;
    }
}
