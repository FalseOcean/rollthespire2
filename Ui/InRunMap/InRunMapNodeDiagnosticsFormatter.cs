using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed record InRunMapDiagnosticFactView(PredictorKnowledgeFactId Id, string Title,
    string Text, bool CanExplain);
internal sealed record InRunMapPendingCauseView(string Identity, string Text);

// Pure display projection. Never reads a simulation witness or parses a display
// string back into a number. The trace owns values, provenance and causal edges.
internal static class InRunMapNodeDiagnosticsFormatter
{
    private static string L(IUiTextProvider text, string key) => text.Get("predictor.map.diagnostics." + key);
    private static string Ref(PredictorKnowledgeFactId id) => "F" + id.Ordinal;
    private static string RootRef(PredictorKnowledgeRootId id) => "R" + id.Ordinal;

    private static string CauseDescription(string description, IUiTextProvider text)
    {
        int separator = description.IndexOf(" · ", StringComparison.Ordinal);
        string code = separator < 0 ? description : description[..separator];
        string key = "predictor.runtime.plan.pending." + code;
        return text.TryGet(key, out string label) ? label + (separator < 0 ? "" : description[separator..]) : description;
    }

    internal static ImmutableArray<InRunMapPendingCauseView> Pending(
        IEnumerable<PredictorCombatPendingCause> causes, IUiTextProvider text) =>
        PredictorRouteCauseEvidence.OriginalCauses(causes).Select(cause =>
        {
            string key = "predictor.runtime.plan.pending." + cause.Code;
            string label = text.TryGet(key, out string translated) ? translated : cause.Description;
            label = L(text, "kind." + cause.ReasonKind) + ": " + label;
            string source = cause.Choice?.Description ?? cause.ChoiceId ?? cause.EnemySource?.ToString() ?? "";
            string origin = string.Join(" · ", new[] { cause.NodeId, source }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string detail = cause.Code + " · " + cause.Description + "\n" + cause.SourceEvidence;
            if (!cause.RequiredPropertyKeys.IsDefaultOrEmpty)
                detail += "\n" + string.Join(", ", cause.RequiredPropertyKeys);
            return new InRunMapPendingCauseView(cause.Identity,
                (origin.Length == 0 ? label : label + " · " + origin) + "\n" + detail);
        }).ToImmutableArray();

    internal static ImmutableArray<PredictorKnowledgeFactId> RngConsumption(PredictorNodeDiagnostics diagnostics) =>
        diagnostics.After.Where(id =>
        {
            string property = diagnostics.Snapshot.Fact(id).PropertyKey;
            return property.StartsWith("rng.", StringComparison.Ordinal) && property.EndsWith(".consumed", StringComparison.Ordinal);
        }).ToImmutableArray();

    internal static InRunMapDiagnosticFactView Fact(PredictorKnowledgeSnapshot snapshot,
        PredictorKnowledgeFactId id, IUiTextProvider text)
    {
        var fact = snapshot.Fact(id);
        bool safe = fact.Status is PredictorKnowledgeStatus.Known or PredictorKnowledgeStatus.Partial;
        var lines = new List<string>
        {
            L(text, "status") + ": " + L(text, "status." + fact.Status) + " · " +
                L(text, "origin") + ": " + L(text, "origin." + fact.Origin),
            L(text, "value") + ": " + (safe ? fact.DisplayValue ?? L(text, "structured_value") :
                L(text, "status." + fact.Status)),
            L(text, "boundary") + ": " + fact.Boundary + " · " + L(text, "node") + ": " + fact.NodeId
        };
        if (safe)
            lines.AddRange(fact.ProvenDetails.Select(d => d.Key == "range-kind" && d.Text == "conservative enclosure"
                ? L(text, "conservative_range") : d.Key + ": " + d.Text));
        if (!string.IsNullOrWhiteSpace(fact.Derivation)) lines.Add(L(text, "derivation") + ": " + fact.Derivation);
        return new(id, Ref(id) + " · " + fact.PropertyKey, string.Join("\n", lines),
            fact.Status != PredictorKnowledgeStatus.Known);
    }

    internal static string Operations(PredictorKnowledgeSnapshot snapshot, string nodeId, IUiTextProvider text)
    {
        var operations = snapshot.Operations.Where(o => o.NodeId == nodeId).Select(operation =>
        {
            string reads = string.Join(", ", operation.AllReadFactIds.Select(id =>
                Ref(id) + " " + snapshot.Fact(id).PropertyKey));
            string writes = string.Join(", ", operation.ProducedFactIds.Select(id =>
                Ref(id) + " " + snapshot.Fact(id).PropertyKey));
            return "O" + operation.Id.Ordinal + " · " + operation.Name + " · " + operation.Boundary + "\n" +
                L(text, "reads") + ": " + (reads.Length == 0 ? L(text, "none") : reads) + "\n" +
                L(text, "writes") + ": " + writes;
        });
        return string.Join("\n\n", operations);
    }

    internal static string Explain(PredictorKnowledgeSnapshot snapshot, PredictorKnowledgeFactId id,
        IUiTextProvider text)
    {
        var explanation = snapshot.Explain(id);
        if (explanation.Roots.IsEmpty) return L(text, "no_unresolved_dependency");
        var lines = new List<string> { L(text, "original_roots") };
        foreach (var root in explanation.Roots)
        {
            lines.Add(RootRef(root.Id) + " · " + L(text, "kind." + root.Kind) + " · " + CauseDescription(root.Description, text));
            lines.Add(L(text, "node") + ": " + root.NodeId + " · " + L(text, "choice") + ": " +
                (root.ChoiceId ?? L(text, "none")));
            foreach (var input in root.RequiredInputs)
                lines.Add(L(text, "required_input") + ": " + input.Key + " · " + CauseDescription(input.Description, text));
            if (root.RequiredInputs.IsEmpty) lines.Add(L(text, "no_player_input"));
        }
        lines.Add("\n" + L(text, "dependency_graph"));
        foreach (var fact in explanation.Facts)
        {
            var operation = snapshot.Operations[fact.OperationId.Ordinal - 1];
            lines.Add(Ref(fact.Id) + " · " + fact.PropertyKey + " · " + fact.NodeId + " · " +
                L(text, "status." + fact.Status) + " · " + operation.Name);
            foreach (var root in fact.DirectRootIds)
                lines.Add("  " + RootRef(root) + " → " + Ref(fact.Id));
        }
        foreach (var edge in explanation.CausalEdges)
            lines.Add("  " + Ref(edge.Input) + " → " + Ref(edge.Result));
        return string.Join("\n", lines);
    }
}
