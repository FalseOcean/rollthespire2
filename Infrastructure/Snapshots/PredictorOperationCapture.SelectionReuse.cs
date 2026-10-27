using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private sealed record SelectionCapture(PredictorOperationSnapshot Snapshot, string Version, string? DirectStamp);
    // A native selector owns this observation. Leaving a card cancels its
    // worker/view, while the selector's frozen inputs remain reusable only
    // after a fresh dynamic/source/pool observation. No result is cached here.
    private static readonly ConditionalWeakTable<NDeckTransformSelectScreen, SelectionCapture> SelectionCaptures = new();
    private sealed record ContentIdentity(long Id);
    private static readonly ConditionalWeakTable<object, ContentIdentity> ContentIdentities = new();
    private static long _lastContentIdentity;
    // Separate from detached card instance ids: observing registrations cannot
    // advance the predicted NextInstanceId or rely on collision-prone hashes.
    private static long ContentId(object model) => ContentIdentities.GetValue(model,
        _ => new(Interlocked.Increment(ref _lastContentIdentity))).Id;

    internal static NDeckTransformSelectScreen? TransformSelector(NCardHolder owner)
    {
        for (Node? node = owner; node != null; node = node.GetParent())
            if (node is NDeckTransformSelectScreen screen) return screen;
        return null;
    }

    internal static PredictorOperationSnapshot CaptureSelection(RunState run, string version,
        NDeckTransformSelectScreen selector, Node? root, out string observationStamp, out bool direct,
        CaptureTiming? timing = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        long allocated = timing == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        SelectionCaptures.TryGetValue(selector, out var previous);
        if (previous?.Version == version && previous.DirectStamp != null &&
            TryDirectTransformObservation(previous.Snapshot, run, selector, out var current, timing) && current == previous.DirectStamp)
        {
            observationStamp = current!; direct = true;
            if (timing != null)
            {
                timing.SnapshotReuses++;
                timing.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            return previous.Snapshot;
        }
        // For events/composite continuations only the immutable canonical
        // upgrade definitions are reused. All dynamic inputs are captured.
        var snapshot = Capture(run, version, root, true, out observationStamp, timing,
            previous?.Version == version ? previous.Snapshot : null);
        direct = TryDirectTransformObservation(snapshot, run, selector, out var stamp, timing);
        SelectionCaptures.Remove(selector);
        SelectionCaptures.Add(selector, new(snapshot, version, direct ? stamp : null));
        if (direct) observationStamp = stamp!;
        if (timing != null) timing.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return snapshot;
    }
}
