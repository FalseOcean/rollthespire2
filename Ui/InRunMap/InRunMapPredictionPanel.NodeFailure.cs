using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapPredictionPanel
{
    private void ShowInformationNode((int ActIndex, MapPosition Point) location,
        InRunMapNodeDetailData data, PointVisual visual)
    {
        try { _details.ShowNode(data, GlobalRegion(visual), _screen.GetGlobalRect(), visual.Node); }
        catch (Exception ex) { NodeInformationFailure(location, data.NodeId, "node-view", ex); }
    }

    private void ShowInformationEditor((int ActIndex, MapPosition Point) location, bool diagnostics)
    {
        if (_nodeInformationFailures.Contains(location.Point)) { _details.CloseEditor(); return; }
        try
        {
            var data = diagnostics ? _presenter.DiagnosticView(location) : _presenter.Editor(location);
            if (data != null) _details.ShowEditor(data, _screen.GetGlobalRect());
            else _details.CloseEditor();
        }
        catch (Exception ex)
        {
            string nodeId = _prediction.Nodes.TryGetValue(location, out var node) ? node.Position.NodeId :
                _prediction.TerminalBoundaries[location].TargetPosition.NodeId;
            NodeInformationFailure(location, nodeId, "node-editor", ex);
        }
    }

    private void NodeInformationFailure((int ActIndex, MapPosition Point) location, string nodeId,
        string stage, Exception error)
    {
        RuntimeLog.WarnException($"inRunRouteNodeInformationFailed=true stage={stage} revision={_prediction.Revision} " +
            $"act={location.ActIndex + 1} point={location.Point} node={nodeId} " +
            $"seed={_prediction.Snapshot?.Run.Context.Seed}", error);
        _presenter.DiscardNodeProjection(location);
        _nodeInformationFailures.Add(location.Point);
        if (_details.HoverNodeId == nodeId) _details.CloseHover();
        if (_details.EditorNodeId == nodeId) _details.CloseEditor();
        string message = _text.Format("predictor.map.node_information_failed", error.Message);
        long revision = _prediction.Revision;
        _nodeDetails[location.Point] = new(nodeId, _text.Get("predictor.map.information_failed"), "", [message], [], [])
        {
            IsCurrent = () => revision == _prediction.Revision && _nodeInformationFailures.Contains(location.Point)
        };
        if (_annotations.Remove(location.Point, out var previous))
        { _overview.RemoveChild(previous); previous.QueueFree(); }
        var surface = new PanelContainer
        {
            Name = $"FailedObservation_{location.Point.Column}_{location.Point.Row}",
            MouseFilter = MouseFilterEnum.Ignore
        };
        var label = new Label
        {
            Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(245, 0), MouseFilter = MouseFilterEnum.Ignore
        };
        MainPanelTheme.ApplyFont(label); label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color("e3b0a1")); surface.AddChild(label);
        _overview.AddChild(surface); _annotations[location.Point] = surface;
    }
}
