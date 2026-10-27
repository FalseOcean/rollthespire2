using RolltheSpire2.Presentation.Localization;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunOperations;

internal static partial class TransformConfirmationPreview
{
    static TransformConfirmationPreview()
    {
        InRunFeaturePreferences.OperationChanged += RefreshCurrent;
        InRunFeaturePreferences.OperationPolicyChanged += () => RefreshCurrent(PredictionEnabled);
    }
    internal static bool PredictionEnabled => InRunFeaturePreferences.OperationPredictionEnabled;
    private static WeakReference<NTransformPreview>? _forecastOwner;

    private static void RefreshCurrent(bool enabled)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            NTransformPreview? previous = null;
            if (_forecastOwner?.TryGetTarget(out var tracked) == true && GodotObject.IsInstanceValid(tracked)) previous = tracked;
            if (NOverlayStack.Instance?.Peek() is not NDeckTransformSelectScreen screen ||
                PredictorOperationCapture.ReadNullable(screen, "_transformPreview") is not NTransformPreview owner ||
                !owner.IsVisibleInTree()) return;
            bool useRt2 = enabled && PredictionEnabled && IsSupported(owner);
            if (!useRt2 && previous != null)
            {
                previous.Uninitialize();
                foreach (Node child in PredictorOperationCapture.Read<Control>(previous, "_after").GetChildren())
                    if (child is CanvasItem item) item.Hide();
                _forecastOwner = null;
            }
            if (!useRt2 && !ReferenceEquals(owner, previous)) return;
            var selected = PredictorOperationCapture.Read<IEnumerable<CardModel>>(screen, "_selectedCards");
            var native = PredictorOperationCapture.Read<Func<CardModel, CardTransformation>>(screen, "_cardToTransformation");
            // Re-enter only the display initializer. Its patched prefix consults
            // the new preference: disabled restores vanilla's own preview;
            // enabled captures the current selection afresh. Never select,
            // confirm or invoke the real transformation completion callback.
            owner.Initialize(selected.Select(native));
        }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=transformConfirmationPreference", error); }
    }

    internal static bool IsSupported(NTransformPreview owner)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (NOverlayStack.Instance?.Peek() is not NDeckTransformSelectScreen screen || !screen.IsAncestorOf(owner) ||
            !PredictorOperationCapture.SelectionIsPending(screen) || OperationSurfaceHoverCapture.CurrentRun() is not { } run ||
            LocalContext.GetMe(run) is not { } player) return false;
        var obtain = PredictorOperationCapture.ObservedObtainOrigin(player, NGame.Instance);
        string source = obtain?.Relic.Id.Entry ?? (run.CurrentRoom as EventRoom)?.LocalMutableEvent.Id.Entry ?? "";
        bool supported = obtain != null ? source is "NEW_LEAF" or "ASTROLABE" : source is
            "AROMA_OF_CHAOS" or "MORPHIC_GROVE" or "SYMBIOTE" or "WHISPERING_HOLLOW" or "ENDLESS_CONVEYOR" or "TRIAL";
        if (!supported) return false;
        if (owner.GetNodeOrNull<PolicyObserver>("Rt2TransformPolicy") == null)
            owner.AddChild(new PolicyObserver { Name = "Rt2TransformPolicy" });
        var rf = RandomForeseerCompatibility.Capture();
        string feature = (obtain != null ? "relic:" : "event:") + source;
        bool transformEffects = InRunFeaturePreferences.GetOperationMode("extra:transform-effects") != OperationPredictionMode.Off &&
            NeowOperationHoverController.HasTransformExtras(player);
        bool chain = InRunFeaturePreferences.GetOperationMode("extra:obtain-chain") != OperationPredictionMode.Off &&
            obtain?.HasParentObtain == true;
        return OperationPredictionPolicy.Wants(InRunFeaturePreferences.GetOperationMode(feature), rf.IsLoaded,
            transformEffects || chain);
    }

    private sealed partial class PolicyObserver : Node
    {
        private double _poll;
        private string? _signature;
        public override void _Process(double delta)
        {
            if (GetParent() is not NTransformPreview owner || !owner.IsVisibleInTree() || (_poll -= delta) > 0) return;
            _poll = .5;
            string signature = RandomForeseerCompatibility.Capture().Signature;
            if (_signature != null && _signature != signature) RefreshCurrent(PredictionEnabled);
            _signature = signature;
        }
    }

    internal static void ShowUnavailable(NTransformPreview owner)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _forecastOwner = new(owner);
        owner.Uninitialize();
        var after = PredictorOperationCapture.Read<Control>(owner, "_after");
        foreach (Node child in after.GetChildren())
            if (child is CanvasItem item) item.Hide();
        after.AddChild(new Label
        {
            Text = JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode).Get("operation.transformUnavailable"),
            Position = new Vector2(80, -80), CustomMinimumSize = new Vector2(350, 80),
            MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None
        });
    }

    // Called before native Initialize materializes its lazy transformation list.
    // Only this display argument changes: the selector's callback, selected
    // cards, completion result and real transformation remain owned by vanilla.
    internal static void Prepare(NTransformPreview owner, ref IEnumerable<CardTransformation> transformations)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _forecastOwner = new(owner);
        NDeckTransformSelectScreen? screen = null;
        for (Node? node = owner.GetParent(); node != null; node = node.GetParent())
            if (node is NDeckTransformSelectScreen found) { screen = found; break; }
        if (screen == null || !ReferenceEquals(NOverlayStack.Instance?.Peek(), screen) ||
            !PredictorOperationCapture.SelectionIsPending(screen) || OperationSurfaceHoverCapture.CurrentRun() is not { } run ||
            LocalContext.GetMe(run) is not { } player)
            throw new InvalidOperationException("TransformConfirmationNativeSelectionChanged");
        var selected = PredictorOperationCapture.Read<IEnumerable<CardModel>>(screen, "_selectedCards").ToArray();
        if (selected.Length == 0 || selected.Any(card => !player.Deck.Cards.Any(deck => ReferenceEquals(deck, card))))
            throw new InvalidOperationException("TransformConfirmationSelectedCardsInvalid");
        var snapshot = PredictorOperationCapture.CaptureSelection(run, ModRuntime.Snapshot.Detection.NormalizedVersion,
            screen, NGame.Instance, out _, out _);
        if (TransformSelectionForecast.Source(snapshot) == null)
            throw new InvalidOperationException("TransformConfirmationSupportedSourceNotCaptured");
        var ids = selected.Select(PredictorLiveRunCapture.Id).ToImmutableArray();
        var forecasts = TransformSelectionForecast.Build(snapshot, ids);
        // Materialize every display card before changing the argument: a failed
        // reconstruction must never leave a partial forecast paired to originals.
        var display = forecasts.Select(item => OperationPreviewCardModel.Create(item.Card)).ToArray();
        // Display reconstruction executes native upgrade/enchantment methods.
        // Keep the complete post-reconstruction check: main-thread synchronous
        // execution alone does not establish that these cannot re-enter hooks.
        if (!PredictorOperationCapture.IsCurrent(snapshot, run, ModRuntime.Snapshot.Detection.NormalizedVersion, NGame.Instance))
            throw new InvalidOperationException("TransformConfirmationSnapshotChanged");
        transformations = selected.Select((card, slot) => new CardTransformation(card, display[slot])).ToArray();
    }
}
