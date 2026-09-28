using Godot;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>One report-local workspace for all Acts; consumes the existing immutable map viewer.</summary>
internal sealed partial class MapWorkspace : PanelContainer
{
    private readonly Label _title;
    private readonly Label _seed;
    private readonly Label _status;
    private readonly Button _back;
    private readonly VerticalMapView _map;
    private IUiTextProvider? _text;
    private Control? _opener;
    public int SelectedAct { get; private set; }
    private readonly Button[] _actButtons = new Button[3];
    private bool _hasMap;
    public event Action? Closed;
    public event Action<int>? ActRequested;

    public MapWorkspace()
    {
        Name = "MapWorkspace";
        Visible = false;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 6, 1, 16);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 16);
        AddChild(body);
        var header = new HBoxContainer();
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_title);
        _back = new Button { Name = "BackToPrediction", CustomMinimumSize = new Vector2(40, 40) };
        Ui1Theme.ApplyButton(_back, Ui1ButtonRole.Ghost);
        _back.Pressed += Close;
        header.AddChild(_back);
        body.AddChild(header);
        var tabs = new HBoxContainer { Name = "ActTabs" };
        tabs.AddThemeConstantOverride("separation", 8);
        for (int index = 0; index < _actButtons.Length; index++)
        {
            int act = index + 1;
            var tab = new Button { Name = "MapAct" + act, ToggleMode = true, CustomMinimumSize = new Vector2(108, 40) };
            tab.Pressed += () => SelectAct(act);
            _actButtons[index] = tab;
            tabs.AddChild(tab);
        }
        body.AddChild(tabs);
        var panes = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panes.AddThemeConstantOverride("separation", 20);
        body.AddChild(panes);
        var information = new VBoxContainer { Name = "MapInformation", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2 };
        information.AddThemeConstantOverride("separation", 12);
        _seed = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _status = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        information.AddChild(_seed);
        information.AddChild(_status);
        BuildUnknownControls(information);
        BuildRouteControls(information);
        var informationScroll = new ScrollContainer { Name = "MapInformationScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        informationScroll.AddChild(information);
        panes.AddChild(informationScroll);
        _map = new VerticalMapView { Name = "MapView" };
        _map.StrokeFinished += PreviewStroke;
        _map.StrokeStarted += () => { _suppressUnknownOverlay = true; RefreshUnknownRooms(); };
        var mapRegion = new Control { Name = "MapRegion", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3 };
        var mapSurface = new PanelContainer { Name = "MapSurface" };
        mapSurface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = VerticalMapView.PaperColor,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
        mapSurface.AddChild(_map);
        mapRegion.AddChild(mapSurface);
        panes.AddChild(mapRegion);
        // Keep the paper close to the seven columns instead of stretching across the workspace.
        mapRegion.Resized += () =>
        {
            mapSurface.Size = new Vector2(Math.Min(430, mapRegion.Size.X), mapRegion.Size.Y);
            mapSurface.Position = new Vector2((mapRegion.Size.X - mapSurface.Size.X) * .65f, 0);
        };
    }

    public void Open(int act, string seed, IUiTextProvider text, Control opener)
    {
        if (_routeSeed != seed) ResetRouteContext();
        _routeSeed = seed;
        SelectedAct = act; _opener = opener; _hasMap = false;
        _map.Clear();
        ClearRouteView();
        _seed.Text = seed;
        ApplyLocalization(text);
        _status.Text = text.Get("ui1.map.loading");
        Visible = true;
        _actButtons[act - 1].GrabFocus();
        ActRequested?.Invoke(act);
    }

    public void ApplyLocalization(IUiTextProvider text)
    {
        _text = text;
        _title.Text = text.Get("ui1.map.open");
        _back.Text = text.Get("ui1.map.back");
        for (int index = 0; index < _actButtons.Length; index++)
            _actButtons[index].Text = text.Get(index switch { 1 => Ui1TextKey.AnalysisSectionAct2, 2 => Ui1TextKey.AnalysisSectionAct3, _ => Ui1TextKey.AnalysisSectionAct1 });
        RefreshTabs();
        _map.ApplyLocalization(text);
        if (_hasMap) _status.Text = text.Get("ui1.map.scope");
        RefreshRouteControls();
        RefreshUnknownRooms();
    }

    public void SetStatus(string status) { _hasMap = false; _status.Text = status; _map.Clear(); ClearRouteView(); }
    public void ShowMap(MapPrediction map, Texture2D?[] bosses, Texture2D? ancient)
    {
        if (_text is null || !Visible || map.ActIndex + 1 != SelectedAct) return;
        _map.Bind(map, bosses, ancient, _text);
        _hasMap = true;
        BindRoutes(map);
        _status.Text = _text.Get("ui1.map.scope");
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false; _hasMap = false; _map.Clear(); ClearRouteView();
        Closed?.Invoke();
        if (IsInstanceValid(_opener) && _opener!.IsInsideTree() && _opener.IsVisibleInTree()) _opener.GrabFocus();
        _opener = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (Visible && !_map.IsDrawing && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SelectAct(int act)
    {
        if (!Visible || act < 1 || act > 3) return;
        if (SelectedAct == act) { RefreshTabs(); return; }
        SelectedAct = act;
        SetStatus(_text?.Get("ui1.map.loading") ?? string.Empty);
        RefreshTabs();
        ActRequested?.Invoke(act);
    }

    private void RefreshTabs()
    {
        for (int index = 0; index < _actButtons.Length; index++)
        {
            bool selected = SelectedAct == index + 1;
            _actButtons[index].SetPressedNoSignal(selected);
            Ui1Theme.ApplyButton(_actButtons[index], selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        }
    }
}
