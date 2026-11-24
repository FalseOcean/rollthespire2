using Godot;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

// Isolated visual experiment in the existing standalone host. Never launched by RT2 UI.
public partial class MapViewerSpike : Node
{
    private readonly List<object> _assets = new();
    private string _output = "";
    public override async void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            string Arg(string name) => args[Array.IndexOf(args, name) + 1];
            _output = Path.GetFullPath(Arg("--output"));
            Directory.CreateDirectory(_output);
            // Standalone only; in the actual Mod the game PCK is already mounted.
            if (!ProjectSettings.LoadResourcePack(Arg("--game-pck"), replaceFiles: false))
                throw new InvalidOperationException("Unable to mount game resources");
            var mapAssets = new List<string>();
            void FindMapAssets(string directory)
            {
                foreach (string file in DirAccess.GetFilesAt(directory))
                    if (file.Contains("map", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".import")) mapAssets.Add(directory + "/" + file);
                foreach (string sub in DirAccess.GetDirectoriesAt(directory)) FindMapAssets(directory + "/" + sub);
            }
            FindMapAssets("res://images/atlases");
            File.WriteAllLines(Path.Combine(_output, "map-asset-paths.txt"), mapAssets);
            GetWindow().Size = new Vector2I(1420, 1080);
            RenderingServer.SetDefaultClearColor(new Color("101827"));

            var iconName = typeof(NNormalMapPoint).GetMethod("IconName", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("Beta111 NNormalMapPoint.IconName");
            var visuals = new Dictionary<MapPointType, (Texture2D Icon, Texture2D Outline)>();
            foreach (var type in new[] { MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown,
                         MapPointType.Shop, MapPointType.Treasure, MapPointType.RestSite })
            {
                // Research calls the original private resolver, not a second icon catalog.
                string name = (string)iconName.Invoke(null, new object[] { type })!;
                visuals.Add(type, (Texture(ImageHelper.GetImagePath($"atlases/ui_atlas.sprites/map/icons/{name}.tres")),
                    Texture(ImageHelper.GetImagePath($"atlases/compressed.sprites/map/{name}_outline.tres"))));
            }
            // Only the two fixture identities in this standalone process; no RunState or full game bootstrap.
            ModelDb.Init(new[] { typeof(KaiserCrabBoss), typeof(Neow) });
            if (args.Contains("--analysis-map-card"))
            {
                await AnalysisMapCardChecks.Run(this, _output);
                GetTree().Quit();
                return;
            }
            var boss = ModelDb.Encounter<KaiserCrabBoss>();
            visuals.Add(MapPointType.Boss, (Texture(boss.BossNodePath + ".png"), Texture(boss.BossNodePath + "_outline.png")));
            var ancient = ModelDb.Event<Neow>();
            visuals.Add(MapPointType.Ancient, (ancient.MapIcon, ancient.MapIconOutline));
            _assets.Add(new { Kind = "Ancient", Paths = ancient.MapNodeAssetPaths.ToArray(), Size = ancient.MapIcon.GetSize().ToString() });

            // Inspect the PackedScene donor without entering its gameplay lifecycle.
            PackedScene normal = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("/ui/normal_map_point"));
            SceneState state = normal.GetState();
            ShaderMaterial? donorMaterial = null;
            for (int n = 0; n < state.GetNodeCount(); n++)
                if (state.GetNodeName(n).ToString() == "Icon")
                    for (int p = 0; p < state.GetNodePropertyCount(n); p++)
                        if (state.GetNodePropertyName(n, p).ToString() == "material")
                            donorMaterial = state.GetNodePropertyValue(n, p).AsGodotObject() as ShaderMaterial;
            if (donorMaterial is null) throw new InvalidOperationException("Normal icon material not found in scene");
            // Shared official material must never be mutated.
            var localMaterial = (ShaderMaterial)donorMaterial.Duplicate();
            localMaterial.SetShaderParameter("map_color", new Color("dbc99e"));
            PackedScene dotScene = PreloadManager.Cache.GetScene("res://scenes/ui/map_dot.tscn");
            var dotProbe = dotScene.Instantiate<TextureRect>();
            _assets.Add(new { Kind = "DotScene", Texture = dotProbe.Texture?.ResourcePath, Scripted = dotProbe.GetScript().VariantType != Variant.Type.Nil });
            dotProbe.Free();

            var points = Fixture();
            var graph = MapViewerSpikeControl.Capture(points, 7);
            // Mutation after capture must not change the viewer's frozen data.
            var oldType = points[1].PointType;
            points[1].PointType = MapPointType.Unassigned;
            if (graph.Points[1].Type != oldType) throw new InvalidOperationException("Snapshot aliases game MapPoint");
            points[1].PointType = oldType;

            var stage = new Control { Size = new Vector2(1420, 1080), MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(stage);
            var title = Text("RT2 map rendering spike  |  official runtime assets", 24);
            title.Position = new Vector2(28, 16); stage.AddChild(title);
            var subtitle = Text("Synthetic graph / no RunState / bottom to top / same coordinates and Children on both sides", 16);
            subtitle.Position = new Vector2(28, 52); stage.AddChild(subtitle);
            var left = new MapViewerSpikeControl();
            var right = new MapViewerSpikeControl();
            stage.AddChild(left); stage.AddChild(right);
            left.Position = new Vector2(24, 95); right.Position = new Vector2(724, 95);
            left.Render(graph, visuals, localMaterial, null, 672, 880);
            right.Render(graph, visuals, localMaterial, dotScene, 672, 880);
            foreach (var (caption, x) in new[] { ("A  RT2 Line2D", 38), ("B  Original map_dot scene (no random jitter)", 738) })
            { var label = Text(caption, 19); label.Position = new Vector2(x, 83); stage.AddChild(label); }
            var note = Text("All 6 ordinary types + Kaiser Crab placeholder + Neow. Layout and tint are experiment settings, not final UI.", 16);
            note.Position = new Vector2(28, 1010); stage.AddChild(note);
            await Shot("wide.png");
            int leftEdges = left.EdgeCount, rightEdges = right.EdgeCount, dots = right.DotCount;
            // Rebind and width change use the same graph; no stale visual children.
            foreach (var child in stage.GetChildren()) if (child != left) child.Free();
            var narrowTitle = Text("RT2 map spike | narrow width | Line2D", 19);
            narrowTitle.Position = new Vector2(24, 22); stage.AddChild(narrowTitle);
            GetWindow().Size = new Vector2I(560, 1080);
            stage.Size = new Vector2(560, 1080);
            left.Render(graph, visuals, localMaterial, null, 512, 880);
            await Shot("narrow.png");
            if (left.EdgeCount != leftEdges || rightEdges != leftEdges) throw new InvalidOperationException("Rebind edge mismatch");
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(new
            {
                Passed = true, LiveRunStateRequired = false, Nodes = graph.Points.Count, Edges = leftEdges,
                DotInstances = dots, NormalSceneLoaded = normal.ResourcePath, NormalSceneEnteredTree = false,
                MaterialDuplicated = localMaterial != donorMaterial, Shader = donorMaterial.Shader.ResourcePath,
                Types = graph.Points.Select(p => p.Type.ToString()).Distinct().ToArray(), Assets = _assets,
                Checks = new[] { "immutable capture", "endpoint membership", "increasing rows", "column/row placement",
                    "all textures loaded", "Line2D/dot edge equality", "resize/rebind", "no live RunState" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            stage.Free();
            localMaterial.Dispose();
            GD.Print("MAP_VIEWER_SPIKE_PASS " + _output);
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr(ex);
            if (_output.Length != 0) File.WriteAllText(Path.Combine(_output, "failure.txt"), ex.ToString());
            GetTree().Quit(1);
        }
    }

    private Texture2D Texture(string path)
    {
        var texture = ResourceLoader.Load<Texture2D>(path, null, ResourceLoader.CacheMode.Reuse)
            ?? throw new InvalidOperationException("Missing texture " + path);
        _assets.Add(new { Path = path, Type = texture.GetType().Name, Size = texture.GetSize().ToString() });
        return texture;
    }
    private async Task Shot(string name)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(_output, name)) != Error.Ok) throw new IOException("Screenshot failed");
    }
    internal static Label Text(string text, int size)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
    private static MapPoint[] Fixture()
    {
        MapPoint P(int c, int r, MapPointType t) => new(c, r) { PointType = t };
        var points = new[] { P(3,0,MapPointType.Ancient), P(1,1,MapPointType.Monster), P(5,1,MapPointType.Monster),
            P(0,2,MapPointType.Unknown), P(3,2,MapPointType.Shop), P(6,2,MapPointType.Unknown),
            P(1,3,MapPointType.Elite), P(5,3,MapPointType.RestSite), P(3,4,MapPointType.Treasure),
            P(1,5,MapPointType.RestSite), P(5,5,MapPointType.Elite), P(3,6,MapPointType.RestSite), P(3,7,MapPointType.Boss) };
        foreach (var (a,b) in new[] { (0,1),(0,2),(1,3),(1,4),(2,4),(2,5),(3,6),(4,6),(4,7),(5,7),
                     (6,8),(7,8),(8,9),(8,10),(9,11),(10,11),(11,12) }) points[a].AddChildPoint(points[b]);
        return points;
    }
}

// Experiment DTO, not a new public MapPrediction contract or graph framework.
internal sealed record MapViewerSpikePoint(MapCoord Coord, MapPointType Type, IReadOnlyList<MapCoord> Children);
internal sealed record MapViewerSpikeGraph(int Columns, IReadOnlyList<MapViewerSpikePoint> Points);

internal partial class MapViewerSpikeControl : Control
{
    internal int EdgeCount { get; private set; }
    internal int DotCount { get; private set; }
    internal static MapViewerSpikeGraph Capture(IEnumerable<MapPoint> points, int columns) => new(columns,
        Array.AsReadOnly(points.Select(p => new MapViewerSpikePoint(p.coord, p.PointType,
            Array.AsReadOnly(p.Children.Select(c => c.coord).ToArray()))).ToArray()));

    internal void Render(MapViewerSpikeGraph graph, IReadOnlyDictionary<MapPointType, (Texture2D Icon, Texture2D Outline)> visuals,
        ShaderMaterial material, PackedScene? dotScene, float width, float height)
    {
        foreach (var child in GetChildren()) child.Free();
        EdgeCount = DotCount = 0;
        Size = new Vector2(width, height);
        int maxRow = graph.Points.Max(p => p.Coord.row);
        float dx = (width - 112) / (graph.Columns - 1), dy = (height - 150) / maxRow;
        Vector2 At(MapCoord c) => new(56 + c.col * dx, height - 65 - c.row * dy);
        var lookup = graph.Points.ToDictionary(p => p.Coord);
        var edgeLayer = new Node2D(); AddChild(edgeLayer);
        for (int col = 0; col < graph.Columns; col++)
        {
            var label = MapViewerSpike.Text("c" + col, 12);
            label.Position = new Vector2(46 + col * dx, 20); label.Modulate = new Color("8195ac"); AddChild(label);
        }
        foreach (var p in graph.Points)
        {
            if (p.Coord.col < 0 || p.Coord.col >= graph.Columns) throw new InvalidOperationException("Invalid column");
            foreach (var child in p.Children)
            {
                if (!lookup.ContainsKey(child) || child.row <= p.Coord.row || At(child).Y >= At(p.Coord).Y)
                    throw new InvalidOperationException("Invalid edge/vertical placement");
                Vector2 direction = (At(child) - At(p.Coord)).Normalized();
                Vector2 a = At(p.Coord) + direction * 32, b = At(child) - direction * 32;
                if (dotScene is null)
                    edgeLayer.AddChild(new Line2D { Points = new[] { a, b }, Width = 2.2f, Antialiased = true,
                        DefaultColor = new Color("b7a57d") });
                else
                {
                    int count = Math.Max(1, (int)(a.DistanceTo(b) / 13));
                    for (int i = 0; i < count; i++)
                    {
                        var dot = dotScene.Instantiate<TextureRect>();
                        dot.MouseFilter = MouseFilterEnum.Ignore;
                        dot.Scale = Vector2.One * .6f; dot.Modulate = new Color("b7a57d");
                        dot.Position = a.Lerp(b, (i + .5f) / count) - dot.Size * .3f;
                        dot.Rotation = direction.Angle() + Mathf.Pi / 2;
                        edgeLayer.AddChild(dot); DotCount++;
                    }
                }
                EdgeCount++;
            }
        }
        foreach (var p in graph.Points)
        {
            bool special = p.Type is MapPointType.Boss or MapPointType.Ancient;
            float size = special ? 76 : 55;
            var v = visuals[p.Type];
            foreach (bool outline in new[] { true, false })
            {
                var icon = new TextureRect { Texture = outline ? v.Outline : v.Icon,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = Vector2.One * size,
                    Position = At(p.Coord) - Vector2.One * size / 2, MouseFilter = MouseFilterEnum.Ignore,
                    Modulate = outline ? new Color("101827") : Colors.White };
                if (!outline && !special) icon.Material = material;
                AddChild(icon);
            }
            var label = MapViewerSpike.Text(p.Type == MapPointType.Boss ? "Kaiser Crab" : p.Type.ToString(), 12);
            label.Size = new Vector2(96, 20); label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Position = At(p.Coord) + new Vector2(-48, size / 2 + 1); AddChild(label);
            var coord = MapViewerSpike.Text($"{p.Coord.col},{p.Coord.row}", 10);
            coord.Position = At(p.Coord) + new Vector2(-11, -size / 2 - 15); coord.Modulate = new Color("8195ac"); AddChild(coord);
        }
    }
}
