using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Infrastructure.Diagnostics;
using CoreRoomType = RolltheSpire2.Core.Prediction.Maps.MapPointType;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

// Pure editor interaction. Fixed endpoints are decoration, never authored requirements.
internal sealed partial class MapRouteDrawingControl : Control
{
    private static readonly StandardMapRoomType[] Rooms = [StandardMapRoomType.Monster, StandardMapRoomType.Elite,
        StandardMapRoomType.RestSite, StandardMapRoomType.Unknown, StandardMapRoomType.Shop];
    private List<MapDrawingNodeSnapshot> _nodes = [];
    private WorkspacePalette _palette = WorkspacePalette.MainPanel;
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private Action? _changed;
    private readonly Dictionary<StandardMapRoomType, Rect2> _paletteRects = [];
    private readonly Dictionary<long, Rect2> _nodeRects = [];
    private readonly Dictionary<long, Rect2> _groupRects = [];
    private readonly List<(int Start, int Count, Rect2 Rect)> _blocks = [];
    private readonly HashSet<long> _selected = [];
    private readonly Dictionary<StandardMapRoomType, Texture2D> _roomArt = [];
    private readonly List<string> _missingArtRooms = [];
    private readonly Control _artLayer = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly TextureRect _dragPreview = new()
    {
        Name = "MapNodeDragPreview", Visible = false, MouseFilter = MouseFilterEnum.Ignore,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ZIndex = 1
    };
    private ShaderMaterial? _iconMaterial;
    private Rect2 _startRect, _endRect;
    private Vector2 _press, _pointer;
    private StandardMapRoomType? _dragRoom;
    private long? _dragId;
    private bool _holding, _selecting, _moved;

    internal float ContentHeight { get; private set; }
    internal Rect2 AxisRect { get; private set; }
    internal IReadOnlyDictionary<StandardMapRoomType, Rect2> PaletteRects => _paletteRects;
    internal IReadOnlyDictionary<long, Rect2> NodeRects => _nodeRects;
    internal IReadOnlyDictionary<long, Rect2> GroupRects => _groupRects;
    internal IReadOnlyDictionary<StandardMapRoomType, Texture2D> RoomArt => _roomArt;
    internal IReadOnlyList<string> MissingArtRooms => _missingArtRooms;

    internal void Configure(float width, List<MapDrawingNodeSnapshot> nodes, WorkspacePalette palette,
        IUiTextProvider text, Action changed)
    {
        _nodes = nodes;
        _palette = palette;
        _text = text;
        _changed = changed;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        AddChild(_artLayer);
        LoadRoomArt();
        _dragPreview.Material = _iconMaterial;
        AddChild(_dragPreview);
        Size = new(width, 1);
        LayoutDrawing(width);
        SetProcessInput(true);
    }

    private void LayoutDrawing(float width)
    {
        _paletteRects.Clear(); _nodeRects.Clear(); _groupRects.Clear(); _blocks.Clear();
        const float gap = 6, nodeWidth = 40, nodeHeight = 40;
        float paletteWidth = Math.Max(56, Math.Min(82, (width - 24) / 5));
        float x = 0, y = 0;
        foreach (StandardMapRoomType room in Rooms)
        {
            if (x > 0 && x + paletteWidth > width) { x = 0; y += 60; }
            _paletteRects[room] = new(x, y, paletteWidth, 54);
            x += paletteWidth + gap;
        }
        float axisTop = y + 62;
        float axisWidth = Math.Max(40, width - 20);
        float cursorX = 10, cursorY = axisTop + 20, rowHeight = 0;
        Rect2 Place(float blockWidth, float blockHeight)
        {
            if (cursorX > 10 && cursorX + blockWidth > width - 10)
            { cursorX = 10; cursorY += rowHeight + 20; rowHeight = 0; }
            Rect2 rect = new(cursorX, cursorY, blockWidth, blockHeight);
            cursorX += blockWidth + 10;
            rowHeight = Math.Max(rowHeight, blockHeight);
            return rect;
        }
        _startRect = Place(Math.Min(48, axisWidth), 56);
        for (int index = 0; index < _nodes.Count;)
        {
            int first = index;
            long group = _nodes[index].GroupId;
            int count = 1;
            if (group != 0)
                while (index + count < _nodes.Count && _nodes[index + count].GroupId == group) count++;
            if (group == 0)
            {
                Rect2 rect = Place(nodeWidth, nodeHeight);
                _nodeRects[_nodes[index].Id] = rect;
                _blocks.Add((index, 1, rect));
            }
            else
            {
                int columns = Math.Max(1, Math.Min(count, (int)((axisWidth - 12 + gap) / (nodeWidth + gap))));
                int rows = (count + columns - 1) / columns;
                Rect2 rect = Place(columns * (nodeWidth + gap) - gap + 12, rows * (nodeHeight + gap) - gap + 26);
                _groupRects[group] = rect;
                for (int member = 0; member < count; member++)
                    _nodeRects[_nodes[index + member].Id] = new(rect.Position + new Vector2(6 + member % columns * (nodeWidth + gap),
                        20 + member / columns * (nodeHeight + gap)), new(nodeWidth, nodeHeight));
                _blocks.Add((first, count, rect));
            }
            index += count;
        }
        _endRect = Place(Math.Min(48, axisWidth), 56);
        ContentHeight = Math.Max(axisTop + 96, cursorY + rowHeight + 20);
        AxisRect = new(0, axisTop, width, ContentHeight - axisTop);
        CustomMinimumSize = new(width, ContentHeight);
        Size = new(width, ContentHeight);
        RebuildRoomIcons();
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach ((StandardMapRoomType room, Rect2 rect) in _paletteRects)
        {
            DrawStyleBox(_palette.Box(_palette.Surface, _palette.Line, 1), rect);
            DrawLabel(RoomName(room), RoomLabelRect(room, rect), 14, _palette.Color(_palette.Text));
        }
        DrawStyleBox(_palette.Box(_palette.Surface, _palette.Line, 1), AxisRect);
        foreach ((long group, Rect2 rect) in _groupRects)
        {
            DrawStyleBox(_palette.Box(_palette.Surface, _palette.Selected, 1), rect);
            DrawLabel(_text.Get("query.map.drawing.unordered"), new(rect.Position, new(rect.Size.X, 18)), 11, _palette.Color(_palette.Secondary));
        }
        DrawEndpoint(_startRect, StandardMapRoomType.Monster, "query.map.drawing.fixed_start");
        DrawEndpoint(_endRect, StandardMapRoomType.RestSite, "query.map.drawing.fixed_end");
        foreach (MapDrawingNodeSnapshot node in _nodes)
        {
            Rect2 rect = _nodeRects[node.Id];
            DrawStyleBox(_palette.Box(_selected.Contains(node.Id) ? _palette.SelectedSurface : _palette.Surface,
                _selected.Contains(node.Id) ? _palette.Selected : _palette.Line, 1), rect);
            if (!_roomArt.ContainsKey(node.Room)) DrawLabel(RoomName(node.Room), rect, 11, _palette.Color(_palette.Text));
        }
        // Arrows separate blocks, so a group never looks like several ordered requirements.
        Rect2 previous = _startRect;
        foreach (var block in _blocks)
        { DrawArrow(previous, block.Rect); previous = block.Rect; }
        DrawArrow(previous, _endRect);
        if (_holding && _selecting && _moved)
        {
            Rect2 selection = SelectionRect();
            Color fill = _palette.Color(_palette.Selected); fill.A = .16f;
            DrawRect(selection, fill);
            DrawRect(selection, _palette.Color(_palette.Selected), false, 1);
        }
        else if (_holding && _moved && (_dragRoom is not null || _dragId is not null))
        {
            if (AxisRect.HasPoint(_pointer))
            {
                int insertion = InsertionIndex(_pointer);
                Rect2 marker = insertion < _nodes.Count
                    ? _blocks.First(block => block.Start == insertion).Rect : _endRect;
                Color ink = _palette.Color(_palette.Selected);
                DrawLine(new(marker.Position.X - 12, marker.Position.Y - 4),
                    new(marker.Position.X - 12, marker.End.Y + 4), ink, 3);
            }
            StandardMapRoomType room = _dragRoom ?? _nodes.First(node => node.Id == _dragId).Room;
            Rect2 ghost = new(_pointer + new Vector2(10, -20), new(40, 40));
            DrawStyleBox(_palette.Box(_palette.SelectedSurface, _palette.Selected, 1), ghost);
            if (!_roomArt.ContainsKey(room)) DrawLabel(RoomName(room), ghost, 11, _palette.Color(_palette.Text));
        }
    }

    private void DrawEndpoint(Rect2 rect, StandardMapRoomType room, string label)
    {
        DrawStyleBox(_palette.Box(_palette.Surface, _palette.Line, 1), rect);
        if (!_roomArt.ContainsKey(room)) DrawLabel(RoomName(room), new(rect.Position, new(rect.Size.X, 32)), 11, _palette.Color(_palette.Secondary));
        DrawLabel(_text.Get(label), new(rect.Position + new Vector2(0, 32), new(rect.Size.X, 21)), 10, _palette.Color(_palette.Secondary));
    }

    private void DrawArrow(Rect2 left, Rect2 right)
    {
        if (Math.Abs(left.Position.Y - right.Position.Y) > 1) return;
        float centerY = (left.GetCenter().Y + right.GetCenter().Y) / 2;
        DrawLabel("→", new(left.End.X, centerY - 14, Math.Max(0, right.Position.X - left.End.X), 28), 15, _palette.Color(_palette.Secondary));
    }

    private void DrawLabel(string value, Rect2 rect, int size, Color color)
    {
        Font font = GetThemeDefaultFont();
        Vector2 measured = font.GetStringSize(value, HorizontalAlignment.Left, -1, size);
        DrawString(font, new(rect.Position.X + Math.Max(3, (rect.Size.X - measured.X) / 2),
            rect.Position.Y + (rect.Size.Y - font.GetHeight(size)) / 2 + font.GetAscent(size)), value,
            HorizontalAlignment.Left, Math.Max(0, rect.Size.X - 6), size, color);
    }

    private string RoomName(StandardMapRoomType room) => _text.Get(room switch
    {
        StandardMapRoomType.Monster => "query.map.node.monster", StandardMapRoomType.Elite => "query.map.node.elite",
        StandardMapRoomType.RestSite => "query.map.node.rest", StandardMapRoomType.Shop => "query.map.node.shop",
        _ => "query.map.node.unknown"
    });

    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            long group = _groupRects.FirstOrDefault(pair => pair.Value.HasPoint(mouse.Position)).Key;
            if (group != 0) { Ungroup(group); AcceptEvent(); }
            return;
        }
        if (mouse.ButtonIndex != MouseButton.Left) return;
        GrabFocus();
        _press = _pointer = mouse.Position;
        _selected.Clear(); _dragId = null; _dragRoom = null; _selecting = false; _moved = false;
        foreach (var pair in _paletteRects)
            if (pair.Value.HasPoint(mouse.Position)) { _dragRoom = pair.Key; break; }
        if (_dragRoom is null)
        {
            foreach (var pair in _nodeRects)
                if (pair.Value.HasPoint(mouse.Position)) { _dragId = pair.Key; break; }
            if (_dragId is null && AxisRect.HasPoint(mouse.Position) && !_startRect.HasPoint(mouse.Position) &&
                !_endRect.HasPoint(mouse.Position) && !_groupRects.Values.Any(rect => rect.HasPoint(mouse.Position)))
                _selecting = true;
        }
        _holding = _dragRoom is not null || _dragId is not null || _selecting;
        if (_holding) AcceptEvent();
        QueueRedraw();
    }

    public override void _Input(InputEvent input)
    {
        if (!_holding) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { CancelGesture(); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseMotion motion)
        {
            _pointer = GetGlobalTransformWithCanvas().AffineInverse() * motion.Position;
            _moved |= _pointer.DistanceTo(_press) >= 5;
            if (_selecting)
            {
                Rect2 selection = SelectionRect(); _selected.Clear();
                foreach (var pair in _nodeRects)
                    if (selection.Intersects(pair.Value)) _selected.Add(pair.Key);
                foreach (var pair in _groupRects)
                    if (selection.Intersects(pair.Value))
                        foreach (var node in _nodes.Where(node => node.GroupId == pair.Key)) _selected.Add(node.Id);
            }
            UpdateDragPreview();
            QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse)
        {
            _pointer = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
            _moved |= _pointer.DistanceTo(_press) >= 5;
            bool changed = false;
            if (_selecting && _moved) changed = GroupSelection();
            else if (_dragRoom is { } room)
            {
                if (!_moved || AxisRect.HasPoint(_pointer))
                {
                    long id = _nodes.Select(node => node.Id).DefaultIfEmpty(0).Max() + 1;
                    long added = _nodes.Select(node => node.AddedOrder).DefaultIfEmpty(0).Max() + 1;
                    _nodes.Insert(_moved ? InsertionIndex(_pointer) : _nodes.Count, new(id, room, 0, added));
                    changed = true;
                }
            }
            else if (_dragId is { } id && _moved)
            {
                int old = _nodes.FindIndex(node => node.Id == id);
                if (old >= 0)
                {
                    MapDrawingNodeSnapshot node = _nodes[old];
                    int insert = InsertionIndex(_pointer);
                    _nodes.RemoveAt(old);
                    if (AxisRect.HasPoint(_pointer)) _nodes.Insert(Math.Clamp(insert - (old < insert ? 1 : 0), 0, _nodes.Count), node with { GroupId = 0 });
                    NormalizeGroups(); changed = true;
                }
            }
            CancelGesture();
            GetViewport().SetInputAsHandled();
            if (changed) CommitChange();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationExitTree || what == NotificationWMWindowFocusOut) CancelGesture();
        if (what == NotificationExitTree) { _iconMaterial?.Dispose(); _iconMaterial = null; }
    }

    private Rect2 RoomLabelRect(StandardMapRoomType room, Rect2 rect) => _roomArt.ContainsKey(room)
        ? new(rect.Position + new Vector2(0, 30), new(rect.Size.X, 22)) : rect;

    private void LoadRoomArt()
    {
        try { _iconMaterial = VerticalMapView.CreateIconMaterial(_palette.Color(_palette.Surface)); }
        catch (Exception exception)
        {
            RecordArtFailure(exception, "MapRoomMaterial");
            _missingArtRooms.AddRange(Rooms.Select(RoomName));
        }
        foreach (StandardMapRoomType room in Rooms)
        {
            try
            {
                CoreRoomType type = room switch
                {
                    StandardMapRoomType.Monster => CoreRoomType.Monster,
                    StandardMapRoomType.Elite => CoreRoomType.Elite,
                    StandardMapRoomType.RestSite => CoreRoomType.RestSite,
                    StandardMapRoomType.Shop => CoreRoomType.Shop,
                    StandardMapRoomType.Unknown => CoreRoomType.Unknown,
                    _ => throw new ArgumentOutOfRangeException(nameof(room))
                };
                Texture2D texture = VerticalMapView.ResolveRoomIcon(type)
                    ?? throw new InvalidOperationException($"Native map room texture unavailable: {room}.");
                _roomArt[room] = texture;
            }
            catch (Exception exception)
            {
                RecordArtFailure(exception, $"MapRoomTexture:{room}");
                if (!_missingArtRooms.Contains(RoomName(room))) _missingArtRooms.Add(RoomName(room));
            }
        }
    }

    private static void RecordArtFailure(Exception exception, string stage) => RuntimeIssueLedger.Current.Record(exception,
        "MapEditorArtUnavailable", "ActInformationEditor", stage, "issues.capture_failed", RuntimeIssueSeverity.Warning);

    private void RebuildRoomIcons()
    {
        foreach (Node child in _artLayer.GetChildren()) { _artLayer.RemoveChild(child); child.QueueFree(); }
        void Icon(string name, StandardMapRoomType room, Rect2 rect)
        {
            if (!_roomArt.TryGetValue(room, out Texture2D? texture)) return;
            var icon = new TextureRect { Name = name, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = Vector2.Zero,
                Texture = texture, Material = _iconMaterial, MouseFilter = MouseFilterEnum.Ignore };
            _artLayer.AddChild(icon);
            // Texture assignment can establish a native minimum before IgnoreSize is set.
            // Apply the display rectangle after that contract and tree insertion are complete.
            icon.Position = rect.Position + new Vector2((rect.Size.X - 26) / 2, rect.Size.Y == 40 ? 7 : 3);
            icon.Size = new(26, 26);
        }
        foreach (var pair in _paletteRects) Icon($"PaletteArt{pair.Key}", pair.Key, pair.Value);
        foreach (var node in _nodes) Icon($"NodeArt{node.Id}", node.Room, _nodeRects[node.Id]);
        Icon("FixedStartArt", StandardMapRoomType.Monster, _startRect);
        Icon("FixedEndArt", StandardMapRoomType.RestSite, _endRect);
    }

    private void CancelGesture()
    {
        _holding = _selecting = _moved = false; _dragId = null; _dragRoom = null; _selected.Clear();
        _dragPreview.Hide(); QueueRedraw();
    }

    private void UpdateDragPreview()
    {
        StandardMapRoomType? room = _dragRoom ?? _nodes.FirstOrDefault(node => node.Id == _dragId)?.Room;
        if (!_holding || !_moved || _selecting || room is null || !_roomArt.TryGetValue(room.Value, out Texture2D? texture))
        { _dragPreview.Hide(); return; }
        _dragPreview.Texture = texture;
        _dragPreview.Position = _pointer + new Vector2(17, -13);
        _dragPreview.Size = new(26, 26);
        _dragPreview.Show();
    }

    private Rect2 SelectionRect() => new(new Vector2(Math.Min(_press.X, _pointer.X), Math.Min(_press.Y, _pointer.Y)),
        new Vector2(Math.Abs(_press.X - _pointer.X), Math.Abs(_press.Y - _pointer.Y)));

    private int InsertionIndex(Vector2 point)
    {
        if (_blocks.Count == 0) return 0;
        // Every insertion is at a block boundary. Moving one member never splits the remaining group.
        var nearest = _blocks.OrderBy(block => DistanceToRect(point, block.Rect)).First();
        return point.X < nearest.Rect.GetCenter().X ? nearest.Start : nearest.Start + nearest.Count;
    }

    private static float DistanceToRect(Vector2 point, Rect2 rect)
    {
        float dx = Math.Max(rect.Position.X - point.X, Math.Max(0, point.X - rect.End.X));
        float dy = Math.Max(rect.Position.Y - point.Y, Math.Max(0, point.Y - rect.End.Y));
        return dx * dx + dy * dy;
    }

    private bool GroupSelection()
    {
        int[] selected = _nodes.Select((node, index) => _selected.Contains(node.Id) ? index : -1).Where(index => index >= 0).ToArray();
        if (selected.Length == 0) return false;
        int first = selected.Min(), last = selected.Max();
        var touched = _nodes.Skip(first).Take(last - first + 1).Select(node => node.GroupId).Where(group => group != 0).ToHashSet();
        for (int index = 0; index < _nodes.Count; index++)
            if (touched.Contains(_nodes[index].GroupId)) { first = Math.Min(first, index); last = Math.Max(last, index); }
        if (first == last) return false;
        long groupId = _nodes.Select(node => node.GroupId).DefaultIfEmpty(0).Max() + 1;
        for (int index = first; index <= last; index++) _nodes[index] = _nodes[index] with { GroupId = groupId };
        return true;
    }

    private void Ungroup(long group)
    {
        int first = _nodes.FindIndex(node => node.GroupId == group);
        if (first < 0) return;
        MapDrawingNodeSnapshot[] members = _nodes.Where(node => node.GroupId == group).OrderBy(node => node.AddedOrder).ToArray();
        _nodes.RemoveAll(node => node.GroupId == group);
        _nodes.InsertRange(first, members.Select(node => node with { GroupId = 0 }));
        CommitChange();
    }

    private void NormalizeGroups()
    {
        var singletons = _nodes.Where(node => node.GroupId != 0).GroupBy(node => node.GroupId)
            .Where(group => group.Count() < 2).Select(group => group.Key).ToHashSet();
        for (int index = 0; index < _nodes.Count; index++)
            if (singletons.Contains(_nodes[index].GroupId)) _nodes[index] = _nodes[index] with { GroupId = 0 };
    }

    private void CommitChange()
    {
        LayoutDrawing(Size.X);
        _changed?.Invoke();
    }
}
