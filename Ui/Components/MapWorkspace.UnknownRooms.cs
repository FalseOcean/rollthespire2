using Godot;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class MapWorkspace
{
    private Label _unknownTitle = null!, _unknownPremise = null!, _unknownPrecision = null!;
    private GridContainer _unknownRows = null!;
    private bool _suppressUnknownOverlay;
    internal UnknownRoomSequence? DisplayedUnknownSequence { get; private set; }

    private void BuildUnknownControls(VBoxContainer information)
    {
        var box = new VBoxContainer { Name = "UnknownRoomSequence" };
        box.AddThemeConstantOverride("separation", 6);
        _unknownTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.CardTitle);
        _unknownPremise = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _unknownPrecision = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _unknownRows = new GridContainer { Columns = 2 };
        _unknownRows.AddThemeConstantOverride("h_separation", 16);
        box.AddChild(_unknownTitle); box.AddChild(_unknownPremise); box.AddChild(_unknownPrecision); box.AddChild(_unknownRows);
        information.AddChild(box);
    }

    private void RefreshUnknownRooms()
    {
        if (_text is null) return;
        string T(string key) => _text.Get("ui1.map.unknown." + key);
        foreach (var child in _unknownRows.GetChildren()) child.Free();
        DisplayedUnknownSequence = null;
        _unknownTitle.Text = T("title");
        _unknownPremise.Text = T("premise");
        _unknownPrecision.Text = string.Empty;
        if (_routeMap is null) { _map.SetUnknownRooms(null); return; }
        var prior = Enumerable.Range(1, SelectedAct - 1).Select(act => _commitments.GetValueOrDefault(act)).ToArray();
        bool priorKnown = prior.All(p => p is not null && p.Map.Seed == _routeMap.Seed);
        int priorDraws = priorKnown ? prior.Sum(p =>
        {
            var nodes = p!.Map.Points.ToDictionary(n => n.Position);
            return p.Coordinates.Count(c => nodes[c].Type == MapPointType.Unknown);
        }) : 0;
        bool routeBound = priorKnown && !_suppressUnknownOverlay && _preview is null && CurrentCommitment is not null;
        var sequence = routeBound
            ? OrdinaryUnknownRoomPredictor.ForRoute(CurrentCommitment!, prior.Cast<MapRouteCommitment>().ToArray())
            : OrdinaryUnknownRoomPredictor.Baseline(_routeMap.Seed, priorDraws, _extrema[MapPointType.Unknown].Value);
        DisplayedUnknownSequence = sequence;
        _unknownTitle.Text = T(routeBound ? "route_title" : "title");
        _unknownPrecision.Text = !priorKnown ? string.Format(T("missing_prior"), SelectedAct - 1)
            : string.Format(T(routeBound ? "route_precision" : "baseline_precision"), priorDraws);
        foreach (var step in sequence.Steps)
        {
            var row = Ui1Theme.Label($"{step.Ordinal}. {_text.Get("ui1.map.room." + step.Room)}", Ui1TextRole.Meta);
            row.TooltipText = step.Position is { } position ? $"({position.Column}, {position.Row})" : string.Empty;
            _unknownRows.AddChild(row);
        }
        if (sequence.Steps.Count == 0) _unknownRows.AddChild(Ui1Theme.Label(T("empty"), Ui1TextRole.Muted));
        _map.SetUnknownRooms(routeBound ? sequence.Steps : null);
    }
}
