using Godot;
using System.Reflection;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;
using GameNormalPoint = MegaCrit.Sts2.Core.Nodes.Screens.Map.NNormalMapPoint;
using GamePointType = MegaCrit.Sts2.Core.Map.MapPointType;

namespace RolltheSpire2.Ui.Components;

/// <summary>Pure display of an immutable standard map. All Godot work stays on the UI thread.</summary>
internal sealed partial class VerticalMapView : Control
{
    internal static readonly Color PaperColor = new("c8bda8");
    private MapPrediction? _map;
    private Texture2D?[] _bossIcons = Array.Empty<Texture2D?>();
    private Texture2D? _ancientIcon;
    private IUiTextProvider? _text;
    private ShaderMaterial? _material;
    private Color _backgroundColor = PaperColor;
    private MapPointType? _pointTypeHighlight;
    private static readonly MethodInfo? IconName = typeof(GameNormalPoint).GetMethod("IconName", BindingFlags.NonPublic | BindingFlags.Static);

    public VerticalMapView()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        VisibilityChanged += () => { if (!IsVisibleInTree()) CancelStroke(); };
        Resized += DrawMap;
    }
    public void Bind(MapPrediction map, Texture2D?[] bosses, Texture2D? ancient, IUiTextProvider text)
    {
        CancelStroke();
        _highlight = Array.Empty<MapPosition>(); _stroke.Clear(); _resolvedRooms.Clear();
        _map = map; _bossIcons = bosses; _ancientIcon = ancient; _text = text;
        DrawMap();
    }
    public void SetBackgroundColor(Color color)
    {
        _backgroundColor = color;
        _material?.Dispose();
        _material = null;
        DrawMap();
    }
    public void SetPointTypeHighlight(MapPointType? type)
    {
        _pointTypeHighlight = type;
        DrawMap();
    }
    public void Clear()
    {
        CancelStroke();
        _highlight = Array.Empty<MapPosition>(); _stroke.Clear(); _resolvedRooms.Clear();
        _map = null;
        foreach (var child in GetChildren()) child.Free();
    }
    public void ApplyLocalization(IUiTextProvider text) { _text = text; DrawMap(); }
    public override void _ExitTree() { CancelStroke(); GetWindow().FocusExited -= CancelStroke; _material?.Dispose(); _material = null; }
    private void DrawMap()
    {
        foreach (var child in GetChildren()) child.Free();
        if (_map is null || _text is null) return;
        _material ??= CreateIconMaterial(_backgroundColor);
        float side = Math.Min(48, RouteLayout().Row * .95f);
        float edgeInset = side * .32f;
        Vector2 At(MapPosition p) => StrokePosition(new MapStrokePoint(p.Column, p.Row));
        var selected = _highlight.ToHashSet();
        var selectedEdges = _highlight.Zip(_highlight.Skip(1)).ToHashSet();
        Color accent = _committed ? new Color("187b62") : new Color("bd6515");
        var edges = new Node2D(); AddChild(edges);
        foreach (var p in _map.Points)
            foreach (var child in p.Children)
            {
                Vector2 a = At(p.Position), b = At(child), d = (b - a).Normalized();
                bool active = selectedEdges.Contains((p.Position, child));
                edges.AddChild(new Line2D { Points = new[] { a + d * edgeInset, b - d * edgeInset }, Width = active ? 3.5f : 1.25f,
                    DefaultColor = active ? accent : new Color("756b5c", selected.Count > 0 ? .35f : 1f), Antialiased = true });
            }
        int bossIndex = 0;
        foreach (var p in _map.Points.OrderBy(p => p.Position.Row).ThenBy(p => p.Position.Column))
        {
            Texture2D? icon;
            bool special = p.Type is MapPointType.Boss or MapPointType.Ancient;
            string label = _text.Get("ui1.map.type." + p.Type);
            bool resolved = p.Type == MapPointType.Unknown && _resolvedRooms.ContainsKey(p.Position);
            if (resolved)
            {
                var category = _resolvedRooms[p.Position];
                string path = MegaCrit.Sts2.Core.Helpers.ImageHelper.GetRoomIconPath(GamePointType.Unknown,
                    Enum.Parse<MegaCrit.Sts2.Core.Rooms.RoomType>(category.ToString()), null)!;
                icon = ResourceLoader.Load<Texture2D>(path);
                label = string.Format(_text.Get("ui1.map.unknown.tooltip"), _text.Get("ui1.map.room." + category));
            }
            else if (p.Type == MapPointType.Boss) icon = _bossIcons.ElementAtOrDefault(bossIndex++);
            else if (p.Type == MapPointType.Ancient) icon = _ancientIcon;
            else
            {
                string name = (string?)IconName?.Invoke(null, new object[] { (GamePointType)(int)p.Type })
                    ?? throw new InvalidOperationException("Vanilla map icon resolver unavailable");
                icon = ResourceLoader.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/map/icons/" + name + ".tres");
            }
            if (selected.Contains(p.Position))
            {
                var ring = Enumerable.Range(0, 33).Select(i => At(p.Position) + Vector2.FromAngle(i * Mathf.Tau / 32) * side * .4f).ToArray();
                edges.AddChild(new Line2D { Points = ring, Width = 2, DefaultColor = accent, Antialiased = true });
            }
            if (_pointTypeHighlight == p.Type)
            {
                var ring = Enumerable.Range(0, 33).Select(i => At(p.Position) + Vector2.FromAngle(i * Mathf.Tau / 32) * side * .47f).ToArray();
                edges.AddChild(new Line2D { Points = ring, Width = 1.25f, DefaultColor = new Color("ead39b"), Antialiased = true });
            }
            float iconSide = special ? side * .85f : side;
            var texture = new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = Vector2.One * iconSide,
                Position = At(p.Position) - Vector2.One * iconSide / 2, MouseFilter = MouseFilterEnum.Pass,
                TooltipText = label + $" · {p.Position.Row}" };
            if (selected.Count > 0 && !selected.Contains(p.Position)) texture.Modulate = new Color(1, 1, 1, .38f);
            if (resolved) texture.Name = $"ResolvedUnknown_{p.Position.Column}_{p.Position.Row}";
            if (!special && !resolved) texture.Material = _material;
            AddChild(texture);
            if (icon is null)
            {
                var caption = Ui1Theme.Label(label, Ui1TextRole.Meta);
                caption.Position = At(p.Position) + new Vector2(-65, 0);
                caption.Size = new Vector2(130, 20); caption.HorizontalAlignment = HorizontalAlignment.Center;
                caption.MouseFilter = MouseFilterEnum.Ignore; AddChild(caption);
            }
        }
        _strokeLine = new Line2D { Name = "RouteStroke", Width = 3, DefaultColor = new Color(.1f, .4f, .75f, .45f), Antialiased = true };
        AddChild(_strokeLine);
        UpdateStrokeLine();
    }
    private static ShaderMaterial CreateIconMaterial(Color backgroundColor)
    {
        // Preserve the official initial_color mask. A fresh ShaderMaterial defaults it to
        // black, which recolors the ink instead of the map-paper fill. Do not mutate the
        // shared scene resource or instantiate a gameplay-bound NNormalMapPoint.
        using SceneState state = ResourceLoader.Load<PackedScene>("res://scenes/ui/normal_map_point.tscn").GetState();
        for (int node = 0; node < state.GetNodeCount(); node++)
            if (state.GetNodeName(node).ToString() == "Icon")
                for (int prop = 0; prop < state.GetNodePropertyCount(node); prop++)
                    if (state.GetNodePropertyName(node, prop).ToString() == "material" &&
                        state.GetNodePropertyValue(node, prop).AsGodotObject() is ShaderMaterial donor)
                    {
                        var material = (ShaderMaterial)donor.Duplicate();
                        material.SetShaderParameter("map_color", backgroundColor.Lerp(Colors.Gray, .5f));
                        return material;
                    }
        throw new InvalidOperationException("Vanilla map icon material unavailable");
    }

}
