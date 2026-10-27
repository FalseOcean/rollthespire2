using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionPanel
{
    private bool _recognitionDirty;

    // Native tools own the line, its rendering, network messages and save data.
    // The observer retains only the current local gesture, never old native ink.
    internal void BeginNativeStroke(NMapDrawings drawings, Vector2 position, DrawingMode mode)
    {
        if (!_featureEnabled) return;
        CancelStroke();
        if (!_screen.IsOpen || _actIndex != _run.CurrentActIndex || DisplayedDraft == null ||
            mode is not (DrawingMode.Drawing or DrawingMode.Erasing)) return;
        Vector2 global = drawings.GetGlobalTransform() * position;
        if (!IsMapArea(global)) return;
        _nativeDrawing = drawings;
        _strokeMode = mode == DrawingMode.Erasing ? PredictorMapStrokeMode.Erase : PredictorMapStrokeMode.Draw;
        _details.CloseHover(); _details.SetDrawingSuppressed(true);
        _stroke.Add(LocalMouse(global)); _recognitionDirty = true;
        RecognizeNativeStroke(); _ink.Show();
    }

    internal void SampleNativeStroke(NMapDrawings drawings, Vector2 position)
    {
        if (!_featureEnabled || !ReferenceEquals(_nativeDrawing, drawings)) return;
        Vector2 point = LocalMouse(drawings.GetGlobalTransform() * position);
        if (_stroke.Count != 0 && _stroke[^1].DistanceSquaredTo(point) < 1) return;
        _stroke.Add(point); _recognitionDirty = true;
    }

    private void RecognizeNativeStroke()
    {
        if (!_recognitionDirty || _nativeDrawing == null || DisplayedDraft is not { } draft) return;
        _recognitionDirty = false; _hits.Clear();
        if (_strokeMode == PredictorMapStrokeMode.Erase)
        {
            // Native erase is a brush, not a deleted-stroke notification. Retain
            // the established local policy: only touched selected nodes are removed.
            foreach (var point in draft.Selected)
                if (_visuals.TryGetValue(point, out var visual))
                {
                    var drawingToMap = _map.GetGlobalTransform().AffineInverse() * _nativeDrawing.GetGlobalTransform();
                    float radius = 12 * Math.Max(drawingToMap.X.Length(), drawingToMap.Y.Length());
                    var region = Region(visual).Grow(radius);
                    for (int i = 0; i < _stroke.Count; i++)
                        if (Crosses(region, _stroke[Math.Max(0, i - 1)], _stroke[i])) { _hits.Add(point); break; }
                }
        }
        else
        {
            var targets = _visuals.Select(pair =>
            {
                var region = Region(pair.Value); var center = region.GetCenter();
                return new PredictorMapStrokeTarget(pair.Key, new(center.X, center.Y), Math.Min(region.Size.X, region.Size.Y) * .5f);
            }).ToArray();
            _hits.UnionWith(PredictorMapStrokeRecognition.Recognize(draft.Map, targets,
                _stroke.Select(p => new System.Numerics.Vector2(p.X, p.Y)).ToArray(), draft.Previous, draft.Travel));
        }
        RefreshVisuals();
    }

    internal void EndNativeStroke(NMapDrawings drawings)
    {
        if (!_featureEnabled || !ReferenceEquals(_nativeDrawing, drawings)) return;
        RecognizeNativeStroke();
        var hits = _hits.ToArray(); var mode = _strokeMode;
        CancelStroke();
        if (DisplayedDraft is { } draft && hits.Length != 0)
            draft.ApplyNativeStroke(hits, mode);
        _message = null;
        UpdateStatus();
    }

    internal void ClearNativePlans()
    {
        if (!_featureEnabled) return;
        CancelStroke(); CloseInformation();
        foreach (var draft in _drafts.Values) draft.Reset();
        _prediction.ClearPlans();
        _presenter = new(_prediction, _names, _text);
        ClearAnnotations(); _message = null;
        UpdateStatus();
    }

    internal bool AllowsNativeDrawing(InputEvent input)
    {
        if (!_featureEnabled) return true;
        // Do not intercept release/motion: native tools must finish normally,
        // including a gesture ending over the overlay or outside the map.
        if (input is not InputEventMouseButton { Pressed: true } button) return true;
        if (_nativeDrawing != null) return true;
        if (_details?.EditorOpen == true || _details?.HoverSurfaceContains(button.GlobalPosition) == true) return false;
        if (_details?.HoverNodeId is { } shown && FindObservation(shown) is { } location &&
            _visuals.TryGetValue(location.Point, out var visual) && GlobalRegion(visual).HasPoint(button.GlobalPosition))
            _details.CloseHover();
        return IsMapArea(button.GlobalPosition);
    }
}
