using Godot;
using System.Numerics;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class MapWorkspace
{
    private readonly Dictionary<int, MapRouteCommitment> _commitments = new();
    private readonly Dictionary<MapPointType, MapRouteSet> _extrema = new();
    private readonly Dictionary<MapPointType, Button> _extremaButtons = new();
    private MapPrediction? _routeMap;
    private MapRouteSet? _allRoutes;
    private MapRouteSet? _optimalRoutes;
    private MapPointType? _optimalMetric;
    private MapPosition[]? _preview;
    private BigInteger _routeRank;
    private string _routeSeed = string.Empty;
    private Label _routeHint = null!, _routeState = null!, _routeCount = null!;
    private Button _confirmRoute = null!, _redrawRoute = null!, _clearRoute = null!, _prevRoute = null!, _nextRoute = null!, _allRouteMode = null!;
    public MapRouteCommitment? CurrentCommitment => _commitments.GetValueOrDefault(SelectedAct);
    internal IReadOnlyList<MapPosition>? PreviewRoute => _preview;

    private void BuildRouteControls(VBoxContainer information)
    {
        var controls = new VBoxContainer { Name = "RouteControls" };
        controls.AddThemeConstantOverride("separation", 12);
        information.AddChild(controls);
        _routeHint = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _routeState = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        controls.AddChild(_routeHint); controls.AddChild(_routeState);
        Button AddButton(HBoxContainer parent, string name, Action action)
        {
            var button = new Button { Name = name };
            Ui1Theme.ApplyButton(button, Ui1ButtonRole.Secondary);
            button.Pressed += action; parent.AddChild(button); return button;
        }
        var actions = new HBoxContainer(); controls.AddChild(actions);
        _confirmRoute = AddButton(actions, "ConfirmRoute", CommitRoute);
        _redrawRoute = AddButton(actions, "RedrawRoute", () => { _suppressUnknownOverlay = true; _preview = null; PresentRoute(true); });
        _clearRoute = AddButton(actions, "ClearRoute", () =>
        {
            _commitments.Remove(SelectedAct); _preview = null; _optimalRoutes = null; _optimalMetric = null; PresentRoute(true);
        });
        foreach (var type in new[] { MapPointType.Unknown, MapPointType.Elite, MapPointType.RestSite, MapPointType.Shop, MapPointType.Monster })
        {
            var row = new HBoxContainer(); controls.AddChild(row);
            var button = AddButton(row, "RouteExtremum" + type, () => PreviewExtremum(type));
            _extremaButtons[type] = button;
        }
        _routeCount = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true); controls.AddChild(_routeCount);
        var browse = new HBoxContainer(); controls.AddChild(browse);
        _prevRoute = AddButton(browse, "PreviousRoute", () => BrowseRoute(-1));
        _nextRoute = AddButton(browse, "NextRoute", () => BrowseRoute(1));
        _allRouteMode = AddButton(browse, "AllRoutes", () =>
        {
            _optimalRoutes = null; _optimalMetric = null; _preview = null; PresentRoute(true);
        });
    }

    private void BindRoutes(MapPrediction map)
    {
        ClearRouteView();
        _suppressUnknownOverlay = false;
        _routeMap = map;
        _allRoutes = new MapRouteSet(map);
        foreach (var type in _extremaButtons.Keys) _extrema[type] = new MapRouteSet(map, type, type == MapPointType.Monster);
        if (CurrentCommitment is { } saved && (saved.Map.Seed != map.Seed || saved.Map.Act != map.Act || !_allRoutes.IsLegal(saved.Coordinates)))
            _commitments.Remove(SelectedAct);
        PresentRoute(true);
    }
    private void ClearRouteView()
    {
        _routeMap = null; _allRoutes = null; _optimalRoutes = null; _optimalMetric = null; _preview = null;
        _extrema.Clear(); _routeRank = 0; RefreshRouteControls();
        RefreshUnknownRooms();
    }
    public void ResetRouteContext()
    {
        _commitments.Clear(); _routeSeed = string.Empty; ClearRouteView();
        _map.SetRoute(null, false, true);
    }
    private void PreviewStroke(IReadOnlyList<MapStrokePoint> stroke)
    {
        if (_allRoutes is null) return;
        var set = _optimalRoutes ?? _allRoutes;
        _preview = set.Match(stroke);
        _routeRank = set.Rank(_preview);
        PresentRoute(false);
    }
    private void PreviewExtremum(MapPointType type)
    {
        if (!_extrema.TryGetValue(type, out var set)) return;
        _suppressUnknownOverlay = true;
        _optimalRoutes = set; _optimalMetric = type; _routeRank = 0; _preview = set.At(0);
        PresentRoute(true);
    }
    private void BrowseRoute(int delta)
    {
        if (_optimalRoutes is null) return;
        _routeRank = (_routeRank + delta + _optimalRoutes.Count) % _optimalRoutes.Count;
        _preview = _optimalRoutes.At(_routeRank); PresentRoute(true);
    }
    private void CommitRoute()
    {
        if (_routeMap is null || _preview is null) return;
        _suppressUnknownOverlay = false;
        _commitments[SelectedAct] = new MapRouteCommitment(_routeMap, _preview);
        _preview = null; _optimalRoutes = null; _optimalMetric = null; PresentRoute(true);
    }
    private void PresentRoute(bool clearStroke)
    {
        _map.SetRoute(_preview ?? CurrentCommitment?.Coordinates, _preview is null && CurrentCommitment is not null, clearStroke);
        RefreshRouteControls();
        RefreshUnknownRooms();
    }
    private void RefreshRouteControls()
    {
        if (_text is null) return;
        string T(string key) => _text.Get("ui1.map.route." + key);
        _routeHint.Text = T("hint");
        _routeState.Text = T(_preview is not null ? CurrentCommitment is not null ? "preview_saved" : "preview"
            : CurrentCommitment is not null ? "committed" : "none");
        _confirmRoute.Text = T("confirm"); _redrawRoute.Text = T("redraw"); _clearRoute.Text = T("clear");
        _prevRoute.Text = T("previous"); _nextRoute.Text = T("next"); _allRouteMode.Text = T("all");
        _confirmRoute.Disabled = _preview is null;
        _redrawRoute.Disabled = _allRoutes is null;
        _clearRoute.Disabled = _allRoutes is null || (_preview is null && CurrentCommitment is null && _optimalRoutes is null);
        foreach (var (type, button) in _extremaButtons)
        {
            bool available = _extrema.TryGetValue(type, out var set);
            button.Text = string.Format(T(type == MapPointType.Monster ? "min" : "max"), _text.Get("ui1.map.type." + type), available ? set!.Value.ToString() : "—");
            button.Disabled = !available;
            Ui1Theme.ApplyButton(button, _optimalMetric == type ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        }
        _routeCount.Visible = _optimalRoutes is not null;
        _routeCount.Text = _optimalRoutes is null ? string.Empty :
            string.Format(T("optimal"), _text.Get("ui1.map.type." + _optimalMetric), _optimalRoutes.Value, _routeRank + 1, _optimalRoutes.Count);
        _prevRoute.Visible = _nextRoute.Visible = _allRouteMode.Visible = _optimalRoutes is not null;
        _prevRoute.Disabled = _nextRoute.Disabled = _optimalRoutes is null || _optimalRoutes.Count <= 1;
    }
}
