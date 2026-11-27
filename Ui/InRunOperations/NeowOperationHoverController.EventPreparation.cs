using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationHoverController
{
    private static readonly ConditionalWeakTable<object, object> QueuedEventPreparations = new();

    internal static async void QueueEventHoverPreparation(Control owner)
    {
        try
        {
            if (!InRunFeaturePreferences.OperationPredictionEnabled || !owner.IsInsideTree() ||
                RunManager.Instance.DebugOnlyGetState() is not RunState run || run.CurrentRoom is not EventRoom room)
                return;
            var ev = room.LocalMutableEvent;
            if (LocalContext.GetMe(run) is not { } player || !ReferenceEquals(player, ev.Owner)) return;
            var selector = owner as NDeckTransformSelectScreen;
            if (selector != null ? !PredictorOperationEventCapture.IsRandomTransformEvent(ev.Id.Entry)
                : owner is not NEventOptionButton button || !ReferenceEquals(button.Event, ev)) return;
            object source = (object?)selector ?? ev;
            if (QueuedEventPreparations.TryGetValue(source, out _)) return;
            QueuedEventPreparations.Add(source, new object());

            // Let the native surface finish attaching and give it an opportunity
            // to draw before doing one bounded main-thread preparation.
            var tree = owner.GetTree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(owner) || !owner.IsVisibleInTree() || owner.IsQueuedForDeletion() ||
                !InRunFeaturePreferences.OperationPredictionEnabled || !ReferenceEquals(run, RunManager.Instance.DebugOnlyGetState()) ||
                !ReferenceEquals(run.CurrentRoom, room)) return;
            var modes = InRunFeaturePreferences.CaptureOperationModes();
            var mode = modes.Get("event:" + ev.Id.Entry);
            var features = RandomForeseerCompatibility.Capture().Features;
            if (mode == OperationPredictionMode.Off ||
                mode == OperationPredictionMode.Auto &&
                (selector != null ? features.Transform(selector) : features.Event(owner as NEventOptionButton)) &&
                !(selector != null && modes.Get("extra:transform-effects") != OperationPredictionMode.Off && HasTransformExtras(player)))
                return;

            RuntimeSnapshotThreadGuard.BindCurrentThread();
            string version = ModRuntime.Snapshot.Detection.NormalizedVersion;
            var timing = new PredictorOperationCapture.CaptureTiming();
            long started = Stopwatch.GetTimestamp();
            if (PredictorOperationCapture.TryCaptureEventHover(run, version, selector, out _, out _, out _, timing) &&
                timing.ScopedEventCaptures != 0)
                RuntimeLog.Info("operationEventHoverPreparation=true;source=" + ev.Id + ";elapsedMs=" +
                    Elapsed(started).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + CapturePhases("prepare", timing));
        }
        catch (Exception error) { RuntimeLog.WarnException("operationEventHoverPreparationFailed=true", error); }
    }
}
