using Godot;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class VerticalMapView
{
    private MapPosition[] _highlight = Array.Empty<MapPosition>();
    private bool _committed;
    private bool _drawing;
    private readonly List<MapStrokePoint> _stroke = new();
    private Line2D? _strokeLine;
    public event Action<IReadOnlyList<MapStrokePoint>>? StrokeFinished;
    public event Action? StrokeStarted;
    public bool IsDrawing => _drawing;
    public bool ReadOnly { get; set; }
    public override void _Ready() => GetWindow().FocusExited += CancelStroke;

    public void SetRoute(IReadOnlyList<MapPosition>? path, bool committed, bool clearStroke = false)
    {
        _highlight = path?.ToArray() ?? Array.Empty<MapPosition>();
        _committed = committed;
        if (clearStroke) _stroke.Clear();
        DrawMap();
    }

    private (float Left, float Top, float Column, float Row, int MaxRow) RouteLayout()
    {
        int maxRow = _map?.Points.Max(p => p.Position.Row) ?? 1;
        float row = Math.Min(52, Math.Max(1, (Size.Y - 60) / Math.Max(1, maxRow)));
        float width = Math.Min(Math.Max(1, Size.X - 56), 310);
        return ((Size.X - width) / 2, (Size.Y - maxRow * row) / 2, width / Math.Max(1, (_map?.Columns ?? 7) - 1), row, maxRow);
    }
    internal Vector2 StrokePosition(MapStrokePoint point)
    {
        var l = RouteLayout();
        return new Vector2(l.Left + (float)point.Column * l.Column, l.Top + (l.MaxRow - (float)point.Row) * l.Row);
    }
    private void AppendStroke(Vector2 local)
    {
        var l = RouteLayout();
        var sample = new MapStrokePoint((local.X - l.Left) / l.Column, l.MaxRow - (local.Y - l.Top) / l.Row);
        if (_stroke.Count > 0 && StrokePosition(_stroke[^1]).DistanceSquaredTo(local) < 4) return;
        // Bounded UI history; preserve the whole gesture if an unusually long scribble arrives.
        if (_stroke.Count >= 2048)
            for (int i = _stroke.Count - 2; i > 0; i -= 2) _stroke.RemoveAt(i);
        _stroke.Add(sample);
        UpdateStrokeLine();
    }
    private void UpdateStrokeLine()
    {
        if (IsInstanceValid(_strokeLine)) _strokeLine!.Points = _stroke.Select(StrokePosition).ToArray();
    }
    private void CancelStroke()
    {
        if (!_drawing) return;
        _drawing = false; _stroke.Clear(); UpdateStrokeLine();
    }
    public override void _GuiInput(InputEvent @event)
    {
        if (ReadOnly) return;
        if (_map is not null && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } down)
        {
            _drawing = true; _stroke.Clear(); StrokeStarted?.Invoke(); AppendStroke(down.Position); AcceptEvent();
        }
    }
    public override void _Input(InputEvent @event)
    {
        if (!_drawing) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            CancelStroke(); GetViewport().SetInputAsHandled(); return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            AppendStroke(GetGlobalTransformWithCanvas().AffineInverse() * motion.Position);
            GetViewport().SetInputAsHandled();
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } up)
        {
            AppendStroke(GetGlobalTransformWithCanvas().AffineInverse() * up.Position);
            _drawing = false;
            if (_stroke.Count >= 2) StrokeFinished?.Invoke(Array.AsReadOnly(_stroke.ToArray()));
            else { _stroke.Clear(); UpdateStrokeLine(); }
            GetViewport().SetInputAsHandled();
        }
    }
}
