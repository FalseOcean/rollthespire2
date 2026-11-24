using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private readonly HashSet<string> _diagnosticsExpanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PredictorKnowledgeFactId> _diagnosticExplanations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _diagnosticScrollRequests = new(StringComparer.Ordinal);
    private long _nextDiagnosticScrollRequest;
    private string D(string key) => _text.Get("predictor.map.diagnostics." + key);

    private void DiagnosticsEditor(Context context, EditorParts parts)
    {
        DiagnosticsEditor(context.Node.Position.NodeId, context.Node.Diagnostics, parts,
            (label, apply, selected) => Choice(context, label, _ => apply(), selected: selected));
        if (_diagnosticsExpanded.Contains(context.Node.Position.NodeId) && !context.Node.PendingCauses.IsEmpty)
            parts.Sections.Add(new(D("pending_causes"),
                string.Join("\n", DiagnosticPendingLines(context.Node.PendingCauses)), [], []));
    }

    private void DiagnosticsEditor(string node, PredictorNodeDiagnostics? observation, EditorParts parts,
        Func<string, Func<bool>, bool, InRunMapDetailChoice> choice)
    {
        bool expanded = _diagnosticsExpanded.Contains(node);
        parts.ScrollToTopRequest = Math.Max(parts.ScrollToTopRequest, _diagnosticScrollRequests.GetValueOrDefault(node));
        parts.Sections.Add(new("", "", [], [choice(D(expanded ? "collapse" : "expand"), () =>
        {
            if (!_diagnosticsExpanded.Add(node)) _diagnosticsExpanded.Remove(node);
            return true;
        }, expanded)]));
        if (!expanded) return;
        if (observation is not { } diagnostics)
        { parts.Sections.Add(new(D("title"), D("not_available"), [], [])); return; }
        PredictorKnowledgeFactId? selectedFact = _diagnosticExplanations.TryGetValue(node, out var selected) ? selected : null;
        if (selectedFact is { } focused)
        {
            AddFact(focused);
            parts.Sections.Add(new(D("explanation"),
                InRunMapNodeDiagnosticsFormatter.Explain(diagnostics.Snapshot, focused, _text), [], []));
        }
        AddFacts(D("before"), diagnostics.Before);
        AddFacts(D("after"), diagnostics.After);
        AddFacts(D("results"), diagnostics.Results);
        var consumption = InRunMapNodeDiagnosticsFormatter.RngConsumption(diagnostics);
        if (consumption.IsEmpty)
            parts.Sections.Add(new(D("rng_consumption"), D("no_rng_consumption"), [], []));
        else AddFacts(D("rng_consumption"), consumption);
        parts.Sections.Add(new(D("operations"),
            InRunMapNodeDiagnosticsFormatter.Operations(diagnostics.Snapshot, node, _text), [], []));

        void AddFacts(string title, ImmutableArray<PredictorKnowledgeFactId> ids)
        {
            // The routine table omits bottle bookkeeping. A dependent event's
            // result explanation can still traverse these exact retained facts.
            ids = ids.Where(id => !diagnostics.Snapshot.Fact(id).PropertyKey.StartsWith("potions.", StringComparison.Ordinal)).ToImmutableArray();
            if (ids.IsDefaultOrEmpty) return;
            parts.Sections.Add(new(title, "", [], []));
            foreach (var id in ids.Distinct())
            {
                if (selectedFact != id) AddFact(id);
            }
        }
        void AddFact(PredictorKnowledgeFactId id)
        {
            var fact = InRunMapNodeDiagnosticsFormatter.Fact(diagnostics.Snapshot, id, _text);
            bool explained = selectedFact == id;
            IReadOnlyList<InRunMapDetailChoice> choices = fact.CanExplain
                ? [choice(D(explained ? "hide_reason" : "show_reason"), () =>
                {
                    if (_diagnosticExplanations.TryGetValue(node, out var existing) && existing == id)
                        _diagnosticExplanations.Remove(node);
                    else
                    {
                        _diagnosticExplanations[node] = id;
                        _diagnosticScrollRequests[node] = ++_nextDiagnosticScrollRequest;
                    }
                    return true;
                }, explained)] : [];
            parts.Sections.Add(new(fact.Title, fact.Text, [], choices));
        }
    }

    private void SynchronizeDiagnostics()
    {
        var live = _session.Nodes.Values.Select(n => n.Position.NodeId).ToHashSet(StringComparer.Ordinal);
        live.UnionWith(_session.TerminalBoundaries.Values.Select(t => t.TargetPosition.NodeId));
        _diagnosticsExpanded.RemoveWhere(id => !live.Contains(id));
        // IDs belong to one forecast. Recompute retains expansion intent only;
        // the selected old explanation must not bind to the new graph.
        _diagnosticExplanations.Clear();
        _diagnosticScrollRequests.Clear();
    }

    private IEnumerable<string> PendingLines(IEnumerable<PredictorCombatPendingCause> causes) =>
        InRunMapPrerequisiteProjection.Pending(causes, _text);

    private IEnumerable<string> DiagnosticPendingLines(IEnumerable<PredictorCombatPendingCause> causes) =>
        InRunMapNodeDiagnosticsFormatter.Pending(causes, _text).Select(row => row.Text);
}
