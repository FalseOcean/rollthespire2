using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionPanel : Control
{
    internal const string NodeName = "RolltheSpire2_MapPrediction";
    private NMapScreen _screen = null!;
    private IRunState _run = null!;
    private Control _map = null!;
    private IUiTextProvider _text = null!;
    private InRunMapRouteInk _ink = null!;
    private readonly Dictionary<int, PredictorMapRouteDraft> _drafts = [];
    private readonly Dictionary<MapPosition, PointVisual> _visuals = [];
    private readonly HashSet<MapPosition> _hits = [];
    private readonly List<Vector2> _stroke = [];
    private int _actIndex;
    private NMapDrawings? _nativeDrawing;
    private PredictorMapStrokeMode _strokeMode;
    private string? _message;
    private bool _featureEnabled;
    private int _featureRevision;
    internal bool PredictionEnabled => _featureEnabled;
    internal PredictorMapRouteDraft? DisplayedDraft => _drafts.GetValueOrDefault(_actIndex);
    private sealed record PointVisual(Control Node, Vector2 Scale);

    internal static void EnsureAttached(NMapScreen screen, IRunState run, ModRuntimeSnapshot runtime)
    {
        if (screen.GetNodeOrNull<Node>(NodeName) is { } existing)
        {
            if (existing is InRunMapPredictionPanel attached) attached.RebindRun(run);
            return;
        }
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        string language = UiLanguagePreferences.LanguageCode;
        var panel = new InRunMapPredictionPanel { Name = NodeName, _screen = screen, _run = run,
            _runtime = runtime, _language = language,
            _text = JsonUiTextProvider.CreatePredictorUi13(language), _actIndex = run.CurrentActIndex,
            MouseFilter = MouseFilterEnum.Ignore };
        screen.AddChild(panel);
        panel.OnMapChanged();
        RuntimeLog.Info("inRunMapPredictionAttached=true;nativeMap=true;scope=ActualCurrentAct;realRunWrites=false");
    }

    private void RebindRun(IRunState run)
    {
        RefreshLocalization();
        if (ReferenceEquals(_run, run)) return;
        CancelStroke(); _visuals.Clear();
        _drafts.Clear(); ResetInformation();
        _run = run; _actIndex = run.CurrentActIndex;
        _message = null; _ink.Hide();
        OnMapChanged(); UpdateStatus();
    }

    public override void _Ready()
    {
        _featureEnabled = InRunFeaturePreferences.MapPredictionEnabled;
        _map = _screen.GetNode<Control>("TheMap");
        _ink = new InRunMapRouteInk { Name = "RolltheSpire2_RouteInk", Visible = false };
        _map.AddChild(_ink);
        GetWindow().FocusExited += CancelStroke;
        InitializeInformation();
        InitializeCollectionPicker();
        _forecastStatus.AddThemeColorOverride("font_outline_color", Colors.Black);
        _forecastStatus.AddThemeConstantOverride("outline_size", 4);
        UpdateStatus(); AlignStatus();
        InRunFeaturePreferences.MapChanged += ApplyFeaturePreference;
        ApplyFeaturePreference(_featureEnabled);
    }

    private void ApplyFeaturePreference(bool enabled)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            _featureEnabled = enabled; _featureRevision++; _predictionQueued = false;
            SetProcess(enabled); Visible = enabled;
            if (enabled) { RefreshLocalization(); OnMapChanged(); return; }
            // Keep local mode, route drafts, chosen outcomes and session history.
            // Only the active gesture and rendered observations are transient.
            CancelStroke(); _ink.Hide();
            CloseInformation(); ClearAnnotations();
        }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapFeaturePreferenceFailed=true", ex); }
    }

    public override void _Process(double delta)
    {
        if (!_featureEnabled) return;
        RefreshLocalization();
        UpdateCollectionPicker();
        AlignStatus();
        if (!_screen.IsOpen || !IsVisibleInTree()) return;
        if (!ActiveScreenContext.Instance.IsCurrent(_screen))
        { CancelStroke(); CloseInformation(); return; }
        RecognizeNativeStroke();
        if (PredictionEnabled || _nativeDrawing != null) RefreshVisuals();
        UpdateInformationPositions();
        bool canLeave = _screen.IsTravelEnabled && !_screen.IsTraveling;
        if (canLeave != _lastDepartureBoundary)
        { _lastDepartureBoundary = canLeave; QueueInformationRefresh(); }
    }
    private void AlignStatus()
    {
        if (_forecastStatus == null || !_forecastStatus.Visible) return;
        // Routine mode/counter/provenance chrome is gone. Only an actual
        // failure, missing entry boundary or incompatible drawing needs an explanation.
        float width = Math.Min(480, Math.Max(1, _screen.Size.X - 32));
        _forecastStatus.CustomMinimumSize = new(width, 0);
        _forecastStatus.Size = new(width, _forecastStatus.GetMinimumSize().Y);
        float bottom = _screen.Size.Y - 16;
        if (_screen.GetNodeOrNull<Control>("%DrawingTools") is { } tools && tools.IsVisibleInTree())
        {
            var region = GetGlobalTransform().AffineInverse() * tools.GetGlobalRect();
            if (region.End.X > 16 && region.Position.X < 16 + width)
                bottom = Math.Min(bottom, region.Position.Y - 12);
        }
        _forecastStatus.Position = new(16, Math.Max(16, bottom - _forecastStatus.Size.Y));
    }
    private Rect2? VisibleTopBarRegion()
    {
        // The native TopBar root spans the entire viewport. Its background is
        // the visible strip used for both placement and mouse-input exclusion.
        if (_screen.GetParent() is NGlobalUi { TopBar: { } topBar } && topBar.IsVisibleInTree()
            && topBar.GetNodeOrNull<Control>("BgImage") is { } background && background.IsVisibleInTree())
            return background.GetGlobalTransform() * new Rect2(Vector2.Zero, background.Size);
        return null;
    }

    internal void OnMapChanged()
    {
        if (!_featureEnabled || _map == null) return;
        try
        {
            CancelStroke();
            if (_run.Map is MegaCrit.Sts2.Core.Map.NullActMap) return;
            int actual = _run.CurrentActIndex;
            // A new actual Act owns a new live map and boundary. Old drafts
            // do not provide a route into an Act that has not been entered.
            foreach (int old in _drafts.Keys.Where(index => index != actual).ToArray()) _drafts.Remove(old);
            _actIndex = actual;
            var map = InRunMapSnapshot.Actual(_run);
            var previous = _run.CurrentMapCoord is { } coord ? InRunMapSnapshot.Position(coord) : (MapPosition?)null;
            BindDraft(actual, map, previous, InRunMapSnapshot.Travel(_run));
            ShowActual();
            UpdateStatus();
        }
        catch (Exception ex) { PreviewFailure(ex); }
    }
    internal void OnMapOpened()
    {
        OnMapChanged();
    }
    internal void OnMapClosed()
    {
        CancelStroke(); _ink.Hide(); CloseInformation();
    }
    private void BindDraft(int act, MapPrediction map, MapPosition? previous, PredictorMapTravelAllowance travel)
    {
        if (!_drafts.TryGetValue(act, out var draft)) _drafts.Add(act, new(map, previous, travel));
        else if (!InRunMapSnapshot.SameMap(draft.Map, map) || draft.Previous != previous || draft.Travel != travel)
            draft.Rebase(map, previous, travel);
    }

    private void ShowActual()
    {
        CaptureVisuals(_screen.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>()
            .Where(n => !n.IsQueuedForDeletion())
            .ToDictionary(n => InRunMapSnapshot.Position(n.Point.coord), n => (Control)n));
        _ink.Visible = _featureEnabled && PredictionEnabled && _screen.IsOpen;
    }
    private void CaptureVisuals(IReadOnlyDictionary<MapPosition, Control> nodes)
    {
        _visuals.Clear();
        foreach (var (position, node) in nodes)
            _visuals.Add(position, new(node, node.Scale));
    }
    private Rect2 Region(PointVisual visual)
    {
        // Follow map layout/scroll, but keep native focus and Ancient pulse
        // animation out of drawing hit regions and the information anchor.
        var node = visual.Node;
        var local = new Transform2D(node.Rotation, visual.Scale, 0, Vector2.Zero);
        local.Origin = node.Position + node.PivotOffset - local * node.PivotOffset;
        var toMap = _map.GetGlobalTransform().AffineInverse() * node.GetParent<CanvasItem>().GetGlobalTransform() * local;
        return toMap * new Rect2(Vector2.Zero, node.Size);
    }
    private void RefreshVisuals()
    {
        var draft = DisplayedDraft; var selected = PredictionEnabled ? draft?.Selected.ToHashSet() ?? [] : [];
        // Route ink owns route emphasis. Native points retain their own visuals.
        _ink.Regions = _visuals.ToDictionary(pair => pair.Key, pair => Region(pair.Value));
        _ink.Selected = selected; _ink.Pending = _hits; _ink.Stroke = [];
        _ink.Erasing = _strokeMode == PredictorMapStrokeMode.Erase;
        _ink.Compatible = draft?.IsCompatible != false; _ink.QueueRedraw();
    }

    private void UpdateStatus()
    {
        UpdateStatusText();
        QueueInformationRefresh();
    }
    private void UpdateStatusText()
    {
        if (_forecastStatus == null) return;
        var draft = DisplayedDraft;
        _forecastStatus.Text = _predictionFault != null ? _text.Get(_predictionFault) :
            _message != null ? _text.Get(_message) : draft?.IsCompatible == false ? _text.Get("predictor.map.incompatible") :
            _prediction.WaitingForBoundary && _prediction.Snapshot == null ? _text.Get("predictor.map.waiting_boundary") : "";
        _forecastStatus.Visible = _forecastStatus.Text.Length > 0 && _featureEnabled && _screen.IsOpen;
        AlignStatus();
    }
    private void PreviewFailure(Exception ex)
    {
        RuntimeLog.WarnException($"inRunMapPreviewFailed=true;act={_actIndex + 1}", ex);
        CancelStroke(); _actIndex = _run.CurrentActIndex;
        _message = "predictor.map.unavailable"; ShowActual(); UpdateStatus();
    }

    private bool IsMapArea(Vector2 global)
    {
        if (CollectionPickerBlocksMap(global)) return false;
        if (!_screen.GetGlobalRect().HasPoint(global) || _details?.BlocksMapInput(global) == true) return false;
        foreach (string path in new[] { "%DrawingTools", "%MapLegend", "Back", "%ShareButton" })
            if (_screen.GetNodeOrNull<Control>(path) is { } control && control.IsVisibleInTree() && control.GetGlobalRect().HasPoint(global)) return false;
        if (VisibleTopBarRegion() is { } topBarRegion && topBarRegion.HasPoint(global)) return false;
        return true;
    }
    private Vector2 LocalMouse(Vector2 global) => _map.GetGlobalTransform().AffineInverse() * global;
    // Segment/rectangle clipping uses the frozen native region, independent of
    // rendering scale and event sampling rate; fast strokes cannot skip a node.
    internal static bool Crosses(Rect2 rect, Vector2 from, Vector2 to)
    {
        float enter = 0, leave = 1; Vector2 d = to - from;
        bool Clip(float p, float q)
        {
            if (Math.Abs(p) < .00001f) return q >= 0;
            float t = q / p;
            if (p < 0) { if (t > leave) return false; enter = Math.Max(enter, t); }
            else { if (t < enter) return false; leave = Math.Min(leave, t); }
            return true;
        }
        return Clip(-d.X, from.X - rect.Position.X) && Clip(d.X, rect.End.X - from.X)
            && Clip(-d.Y, from.Y - rect.Position.Y) && Clip(d.Y, rect.End.Y - from.Y);
    }
    internal void CancelStroke()
    {
        _nativeDrawing = null; _recognitionDirty = false; _hits.Clear(); _stroke.Clear();
        _details?.SetDrawingSuppressed(false);
        if (_ink != null) { _ink.Visible = _featureEnabled && PredictionEnabled && _screen.IsOpen; _ink.QueueRedraw(); }
    }
    public override void _ExitTree()
    {
        InRunFeaturePreferences.MapChanged -= ApplyFeaturePreference;
        _featureRevision++;
        GetWindow().FocusExited -= CancelStroke; DisposeInformation();
        if (_ink != null && IsInstanceValid(_ink)) _ink.QueueFree();
    }
}
