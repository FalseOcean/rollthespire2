using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private sealed record TerminalDiagnosticBinding((int ActIndex, MapPosition Point) Location,
        string RequestNodeId, string TargetNodeId, long Revision, Func<bool> Apply);
    private readonly Dictionary<string, TerminalDiagnosticBinding> _terminalDiagnosticChoices = [];

    private InRunMapNodeDetailData? TerminalDetail((int ActIndex, MapPosition Point) location)
    {
        if (!_session.TerminalBoundaries.TryGetValue(location, out var terminal)) return null;
        long revision = _session.Revision;
        long localization = _localizationRevision;
        var data = new InRunMapNodeDetailData(terminal.TargetPosition.NodeId, D("entry_blocked"),
            _text.Format("predictor.runtime.position", terminal.TargetPosition.Act, terminal.TargetPosition.Floor),
            [D("entry_not_entered"), .. PendingLines(terminal.PendingCauses)], [], [])
        { IsCurrent = () => localization == _localizationRevision && revision == _session.Revision &&
            _session.TerminalBoundaries.TryGetValue(location, out var current) &&
            current.TargetPosition.NodeId == terminal.TargetPosition.NodeId };
        _details[location] = data;
        return data;
    }

    private InRunMapNodeEditorData? TerminalEditor((int ActIndex, MapPosition Point) location)
    {
        if (!_session.TerminalBoundaries.TryGetValue(location, out var terminal)) return null;
        var parts = new EditorParts();
        TerminalDiagnostics(location, terminal, terminal.TargetPosition.NodeId, parts);
        var data = new InRunMapNodeEditorData(terminal.TargetPosition.NodeId, D("entry_blocked"),
            parts.Sections, parts.Numbers, parts.Toggles, parts.Selections, M("close_editor"))
        { ScrollToTopRequest = parts.ScrollToTopRequest };
        _editors[location] = data;
        return data;
    }

    private void TerminalDiagnostics((int ActIndex, MapPosition Point) location,
        PredictorRouteTerminalBoundary terminal, string requestNodeId, EditorParts parts)
    {
        parts.Sections.Add(new(D("entry_blocked"), D("entry_not_entered") + "\n" +
            string.Join("\n", PendingLines(terminal.PendingCauses)), [], []));
        DiagnosticsEditor(terminal.TargetPosition.NodeId, terminal.Diagnostics, parts, (label, apply, selected) =>
        {
            string id = Id();
            _terminalDiagnosticChoices[id] = new(location, requestNodeId, terminal.TargetPosition.NodeId, _session.Revision, apply);
            return new(id, label, Selected: selected);
        });
    }
}
