using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using RolltheSpire2.Core.Prediction.Maps;
using MapType = RolltheSpire2.Core.Prediction.Maps.MapPointType;
using GameMapType = MegaCrit.Sts2.Core.Map.MapPointType;

namespace RolltheSpire2.Ui.InRunMap;

// Visual-only future/past map, carried by native TheMap so original dragging and
// wheel scrolling continue to work. It never constructs or binds a RunState.
internal sealed partial class InRunMapDisplay : Control
{
    internal Dictionary<MapPosition, Control> Nodes { get; } = [];
    private readonly List<Material> _materials = [];
    private Control? _background;
    private NMapBg? _nativeBackground;

    internal void Build(MapPrediction map, ActModel act, ulong seed, NMapBg background, NBossMapPoint bossDonor)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _nativeBackground = background;
        var bg = new VBoxContainer { Name = "Background", MouseFilter = MouseFilterEnum.Ignore };
        bg.AddThemeConstantOverride("separation", 0); AddChild(bg); _background = bg;
        foreach (var texture in new[] { act.MapTopBg, act.MapMidBg, act.MapBotBg })
            bg.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = new(0, 1080), Texture = texture });
        MatchBackground();

        var shader = NormalMaterial(act.MapBgColor); _materials.Add(shader);
        var pointTypes = map.Points.ToDictionary(point => point.Position, point => point.Type);
        var bosses = map.Points.Where(p => p.Type == MapType.Boss).OrderBy(p => p.Position.Row).ToArray();
        int gridRows = bosses[0].Position.Row;
        float factor = bosses.Length > 1 ? .9f : 1f;
        float dy = 2325f / Math.Max(1, gridRows - 1) * factor, dx = 1050f / map.Columns;
        var jitter = new Rng(seed, $"map_jitter_{map.ActIndex}");
        foreach (var p in map.Points.OrderBy(p => p.Position.Column).ThenBy(p => p.Position.Row))
        {
            Control node;
            Vector2 at;
            if (p.Type == MapType.Boss)
            {
                int index = Array.IndexOf(bosses, p);
                node = Boss(act, index == 0 ? act.BossEncounter : act.SecondBossEncounter!, bossDonor);
                at = new(-200, (index == 0 ? -1980 : -2280) * factor);
                if (bosses.Length > 1) node.Scale = Vector2.One * .75f;
            }
            else if (p.Position.Row == 0)
            {
                if (p.Type == MapType.Ancient)
                {
                    // Native ancient_map_point.tscn uses a 208px control and
                    // an icon/outline pair, tinted by NAncientMapPoint._Ready.
                    node = new Control { Size = new(208, 208), PivotOffset = new(104, 104), MouseFilter = MouseFilterEnum.Ignore };
                    var icon = Texture(act.Ancient.MapIcon, node.Size);
                    icon.Name = "Icon"; icon.PivotOffset = new(100, 100);
                    icon.ExpandMode = TextureRect.ExpandModeEnum.FitWidth;
                    icon.SelfModulate = StsColors.bossNodeUntraveled;
                    node.AddChild(icon); icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                    var outline = Texture(act.Ancient.MapIconOutline, node.Size);
                    outline.Name = "Outline"; outline.PivotOffset = new(100, 100);
                    outline.ExpandMode = TextureRect.ExpandModeEnum.FitWidth;
                    outline.Modulate = act.MapBgColor; outline.ShowBehindParent = true;
                    icon.AddChild(outline); outline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                    at = new(-80, 720);
                }
                else { node = Normal(p.Type, shader); at = new(-80, 800); }
            }
            else
            {
                node = Normal(p.Type, shader);
                at = new Vector2(-500 + p.Position.Column * dx, 740 - p.Position.Row * dy)
                    + new Vector2(jitter.NextFloat(-21, 21), jitter.NextFloat(-25, 25));
            }
            node.Name = $"RouteNode_{p.Position.Column}_{p.Position.Row}";
            AddChild(node); node.SetAnchorsPreset(LayoutPreset.Center); node.Position = Size * .5f + at;
            Nodes.Add(p.Position, node);
        }
        var paths = new Control { Name = "Paths", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(paths); paths.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MoveChild(paths, 1);
        var dotScene = PreloadManager.Cache.GetScene("res://scenes/ui/map_dot.tscn");
        foreach (var p in map.Points)
            foreach (var child in p.Children)
            {
                var childType = pointTypes[child];
                Vector2 a = Endpoint(Nodes[p.Position], p.Type), b = Endpoint(Nodes[child], childType), d = (b - a).Normalized();
                int count = (int)(a.DistanceTo(b) / 22f) + 1;
                for (int i = 1; i < count; i++)
                {
                    // Native dot scene is a TextureRect without a gameplay script.
                    var dot = dotScene.Instantiate<TextureRect>(); paths.AddChild(dot);
                    dot.SetAnchorsPreset(LayoutPreset.Center);
                    // Native CreatePath offsets the 16px dot by 20px after
                    // converting the point coordinates to the path container.
                    dot.Position = a + d * (i * 22) + new Vector2(20, 20); dot.Rotation = d.Angle() + Mathf.Pi / 2;
                    dot.Modulate = act.MapUntraveledColor; dot.MouseFilter = MouseFilterEnum.Ignore;
                }
            }
    }

    public override void _Process(double delta) => MatchBackground();
    private void MatchBackground()
    {
        if (_background == null || _nativeBackground == null || !IsInstanceValid(_nativeBackground)) return;
        _background.Position = _nativeBackground.Position; _background.Size = _nativeBackground.Size;
    }
    private static Vector2 Endpoint(Control node, MapType type) => type is MapType.Boss or MapType.Ancient
        ? node.Position + node.Size * .5f : node.Position;
    private static TextureRect Texture(Texture2D texture, Vector2 size) => new()
    {
        // Remove the intrinsic texture minimum before assigning the requested
        // native layout size; KeepSize would otherwise enlarge a 92px icon.
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
        Texture = texture, Size = size
    };
    private static Control Normal(MapType type, ShaderMaterial material)
    {
        var resolver = typeof(NNormalMapPoint).GetMethod("IconName", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Native map icon resolver unavailable");
        string name = (string)resolver.Invoke(null, [(GameMapType)(int)type])!;
        var node = new Control { Size = new(56, 56), PivotOffset = new(28, 28), MouseFilter = MouseFilterEnum.Ignore };
        var icon = Texture(ResourceLoader.Load<Texture2D>(ImageHelper.GetImagePath($"atlases/ui_atlas.sprites/map/icons/{name}.tres")), new(92, 92));
        icon.Position = new(-18, -18); icon.PivotOffset = new(46, 46);
        icon.SelfModulate = StsColors.halfTransparentWhite; icon.Material = material; node.AddChild(icon);
        return node;
    }
    private Control Boss(ActModel act, EncounterModel encounter, NBossMapPoint donor)
    {
        var node = new Control { Size = donor.Size, PivotOffset = donor.PivotOffset, MouseFilter = MouseFilterEnum.Ignore };
        // Copy the visual subtree, never the scripted NBossMapPoint lifecycle.
        var visuals = (Node2D)donor.GetNode<Node2D>("SpriteContainer").Duplicate(0);
        visuals.Scale = Vector2.One;
        node.AddChild(visuals);
        var spineNode = visuals.GetNode<Node2D>("SpineSprite");
        var placeholder = visuals.GetNode<TextureRect>("PlaceholderImage");
        if (encounter.BossNodeSpineResource is { } resource)
        {
            placeholder.Hide(); spineNode.Show();
            var spine = new MegaSprite(spineNode);
            if (spine.GetNormalMaterial() is ShaderMaterial donorMaterial)
            {
                var local = (ShaderMaterial)donorMaterial.Duplicate(); _materials.Add(local);
                local.SetShaderParameter("map_color", act.MapBgColor);
                local.SetShaderParameter("black_layer_color", act.MapUntraveledColor);
                spine.SetNormalMaterial(local);
            }
            spine.SetSkeletonDataRes(resource); spine.GetAnimationState().AddAnimation("animation");
        }
        else
        {
            spineNode.Hide(); placeholder.Show();
            placeholder.Texture = PreloadManager.Cache.GetAsset<Texture2D>(encounter.BossNodePath + ".png");
            placeholder.SelfModulate = act.MapUntraveledColor;
            var outline = placeholder.GetNode<TextureRect>("PlaceholderOutline");
            outline.Texture = PreloadManager.Cache.GetAsset<Texture2D>(encounter.BossNodePath + "_outline.png");
            outline.Modulate = Colors.White;
            outline.SelfModulate = act.MapBgColor;
        }
        return node;
    }
    private static ShaderMaterial NormalMaterial(Color paper)
    {
        using var state = PreloadManager.Cache.GetScene("res://scenes/ui/normal_map_point.tscn").GetState();
        for (int n = 0; n < state.GetNodeCount(); n++)
            if (state.GetNodeName(n).ToString() == "Icon")
                for (int p = 0; p < state.GetNodePropertyCount(n); p++)
                    if (state.GetNodePropertyName(n, p).ToString() == "material" && state.GetNodePropertyValue(n, p).AsGodotObject() is ShaderMaterial donor)
                    {
                        var local = (ShaderMaterial)donor.Duplicate();
                        local.SetShaderParameter("map_color", paper.Lerp(Colors.Gray, .5f)); return local;
                    }
        throw new InvalidOperationException("Native map icon material unavailable");
    }
    public override void _ExitTree()
    {
        foreach (var material in _materials) material.Dispose();
        _materials.Clear();
    }
}

internal sealed partial class InRunMapRouteInk : Node2D
{
    // Control gives its layout rectangle to the canvas renderer as the culling
    // rectangle. Map-local coordinates extend above zero when browsing upward;
    // Node2D instead derives its bounds from these actual drawing commands.
    internal IReadOnlyDictionary<MapPosition, Rect2> Regions { get; set; } = new Dictionary<MapPosition, Rect2>();
    internal IReadOnlySet<MapPosition> Selected { get; set; } = new HashSet<MapPosition>();
    internal IReadOnlySet<MapPosition> Pending { get; set; } = new HashSet<MapPosition>();
    internal IReadOnlyList<Vector2> Stroke { get; set; } = [];
    internal bool Erasing { get; set; }
    internal bool Compatible { get; set; } = true;
    public override void _Draw()
    {
        foreach (var (point, region) in Regions)
        {
            bool pending = Pending.Contains(point), selected = Selected.Contains(point);
            if (!pending && !selected) continue;
            var color = pending ? new Color(Erasing ? "ef796f" : "f1c55e") : new Color(Compatible ? "86aaa1" : "ef796f");
            if (!pending) color.A = .75f;
            DrawArc(region.GetCenter(), Math.Min(region.Size.X, region.Size.Y) * .55f, 0, Mathf.Tau, 40, color, pending ? 3 : 1.4f, true);
        }
        if (Stroke.Count > 1) DrawPolyline(Stroke.ToArray(), new Color(.95f, .78f, .37f, .7f), 3, true);
    }
}
