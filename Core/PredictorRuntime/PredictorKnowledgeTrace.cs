using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorKnowledgeStatus { Known, Partial, Unknown, NotObserved }
internal enum PredictorKnowledgeOrigin { Captured, Derived, ApprovedDefault, PlayerInput }
internal enum PredictorKnowledgeRootKind { UserChoice, MissingObservation, ImplementationGap, ExecutionFault, FlowGate, PlanBinding, KnownUnavailable, DerivedDependency }
internal readonly record struct PredictorKnowledgeFactId(Guid ForecastId, int Ordinal);
internal readonly record struct PredictorKnowledgeRootId(Guid ForecastId, int Ordinal);
internal readonly record struct PredictorKnowledgeOperationId(Guid ForecastId, int Ordinal);
internal sealed record PredictorKnowledgeRequiredInput(string Key, string Description);
internal sealed record PredictorKnowledgeDetail(string Key, string Text);

internal sealed record PredictorKnowledgeRoot(PredictorKnowledgeRootId Id, string RootKey,
    string NodeId, string? ChoiceId, PredictorKnowledgeRootKind Kind, string Description,
    ImmutableArray<PredictorKnowledgeRequiredInput> RequiredInputs);

// DisplayValue/ProvenDetails are supplied only after the model has established
// their determinism. This trace neither stores typed execution values nor reads
// a witness. Unknown/NotObserved cannot carry a guessed display payload.
internal sealed record PredictorKnowledgeWrite(string PropertyKey, PredictorKnowledgeStatus Status,
    PredictorKnowledgeOrigin Origin, string? DisplayValue = null,
    ImmutableArray<PredictorKnowledgeFactId> UnresolvedReadFactIds = default,
    ImmutableArray<PredictorKnowledgeRootId> DirectRootIds = default,
    string? Derivation = null, ImmutableArray<PredictorKnowledgeDetail> ProvenDetails = default);

internal sealed record PredictorKnowledgeFact(PredictorKnowledgeFactId Id, string PropertyKey,
    string NodeId, string Boundary, PredictorKnowledgeOperationId OperationId,
    PredictorKnowledgeStatus Status, PredictorKnowledgeOrigin Origin, string? DisplayValue,
    ImmutableArray<PredictorKnowledgeDetail> ProvenDetails, string? Derivation,
    ImmutableArray<PredictorKnowledgeFactId> AllReadFactIds,
    ImmutableArray<PredictorKnowledgeFactId> UnresolvedReadFactIds,
    ImmutableArray<PredictorKnowledgeRootId> DirectRootIds);

internal sealed record PredictorKnowledgeOperation(PredictorKnowledgeOperationId Id, string Name,
    string NodeId, string Boundary, ImmutableArray<PredictorKnowledgeFactId> AllReadFactIds,
    ImmutableArray<PredictorKnowledgeFactId> ProducedFactIds);
internal sealed record PredictorKnowledgeEdge(PredictorKnowledgeFactId Result, PredictorKnowledgeFactId Input);
internal sealed record PredictorKnowledgeInputRequest(PredictorKnowledgeRootId RootId,
    string NodeId, string? ChoiceId, PredictorKnowledgeRequiredInput Input);
internal sealed record PredictorKnowledgeExplanation(PredictorKnowledgeFactId Result,
    ImmutableArray<PredictorKnowledgeFact> Facts, ImmutableArray<PredictorKnowledgeOperation> Operations,
    ImmutableArray<PredictorKnowledgeEdge> CausalEdges, ImmutableArray<PredictorKnowledgeRoot> Roots,
    ImmutableArray<PredictorKnowledgeInputRequest> RequiredInputs);

internal sealed class PredictorKnowledgeSnapshot
{
    internal Guid ForecastId { get; }
    internal ImmutableArray<PredictorKnowledgeFact> Facts { get; }
    internal ImmutableArray<PredictorKnowledgeRoot> Roots { get; }
    internal ImmutableArray<PredictorKnowledgeOperation> Operations { get; }

    internal PredictorKnowledgeSnapshot(Guid forecastId, ImmutableArray<PredictorKnowledgeFact> facts,
        ImmutableArray<PredictorKnowledgeRoot> roots, ImmutableArray<PredictorKnowledgeOperation> operations)
    { ForecastId = forecastId; Facts = facts; Roots = roots; Operations = operations; }

    internal PredictorKnowledgeFact Fact(PredictorKnowledgeFactId id) =>
        id.ForecastId == ForecastId && id.Ordinal > 0 && id.Ordinal <= Facts.Length
            ? Facts[id.Ordinal - 1] : throw new ArgumentException("PredictorKnowledgeFactMissing", nameof(id));

    // Only unresolved causal edges lead to roots. AllReadFactIds remains the
    // complete read audit in this snapshot, not a claim that every read matters
    // to this result. Return a shared subgraph once, never enumerate all paths.
    internal PredictorKnowledgeExplanation Explain(PredictorKnowledgeFactId result)
    {
        _ = Fact(result);
        var visited = new HashSet<PredictorKnowledgeFactId>();
        var roots = new HashSet<PredictorKnowledgeRootId>();
        var edges = new List<PredictorKnowledgeEdge>();
        var pending = new Stack<PredictorKnowledgeFactId>();
        pending.Push(result);
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id)) continue;
            var fact = Fact(id);
            roots.UnionWith(fact.DirectRootIds);
            foreach (var input in fact.UnresolvedReadFactIds)
            { edges.Add(new(id, input)); pending.Push(input); }
        }
        var facts = visited.OrderBy(id => id.Ordinal).Select(Fact).ToImmutableArray();
        var operationIds = facts.Select(f => f.OperationId).ToHashSet();
        var causes = Roots.Where(r => roots.Contains(r.Id)).ToImmutableArray();
        return new(result, facts, Operations.Where(o => operationIds.Contains(o.Id)).ToImmutableArray(),
            edges.OrderBy(e => e.Result.Ordinal).ThenBy(e => e.Input.Ordinal).ToImmutableArray(), causes,
            causes.SelectMany(root => root.RequiredInputs.Select(input =>
                new PredictorKnowledgeInputRequest(root.Id, root.NodeId, root.ChoiceId, input))).ToImmutableArray());
    }
}

// One ledger per forecast recomputation. Append-only local recording publishes
// immutable snapshots; old observations cannot be altered by later recording.
// This intentionally does not infer causes from PredictorCombatPendingCause.
internal sealed class PredictorKnowledgeTrace
{
    internal Guid ForecastId { get; } = Guid.NewGuid();
    private readonly List<PredictorKnowledgeFact> _facts = [];
    private readonly List<PredictorKnowledgeRoot> _roots = [];
    private readonly List<PredictorKnowledgeOperation> _operations = [];
    private readonly Dictionary<string, PredictorKnowledgeRoot> _rootsByKey = new(StringComparer.Ordinal);

    internal PredictorKnowledgeRootId AddRoot(string rootKey, string nodeId, string? choiceId,
        PredictorKnowledgeRootKind kind, string description,
        ImmutableArray<PredictorKnowledgeRequiredInput> requiredInputs = default)
    {
        RequireText(rootKey); RequireText(nodeId); RequireText(description);
        if (!Enum.IsDefined(kind) || choiceId != null && string.IsNullOrWhiteSpace(choiceId) ||
            kind == PredictorKnowledgeRootKind.UserChoice && choiceId == null)
            throw new ArgumentException("PredictorKnowledgeRootInvalid");
        var inputs = requiredInputs.IsDefault ? [] : requiredInputs;
        if (inputs.Any(i => i == null || string.IsNullOrWhiteSpace(i.Key) || string.IsNullOrWhiteSpace(i.Description)) ||
            inputs.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count() != inputs.Length ||
            (kind is PredictorKnowledgeRootKind.UserChoice or PredictorKnowledgeRootKind.MissingObservation) && inputs.IsEmpty)
            throw new ArgumentException("PredictorKnowledgeRequiredInputsInvalid");
        if (_rootsByKey.TryGetValue(rootKey, out var prior))
        {
            if (prior.NodeId != nodeId || prior.ChoiceId != choiceId || prior.Kind != kind ||
                prior.Description != description || !prior.RequiredInputs.SequenceEqual(inputs))
                throw new ArgumentException("PredictorKnowledgeRootConflict", nameof(rootKey));
            return prior.Id;
        }
        var root = new PredictorKnowledgeRoot(new(ForecastId, _roots.Count + 1), rootKey,
            nodeId, choiceId, kind, description, inputs);
        _roots.Add(root); _rootsByKey.Add(rootKey, root);
        return root.Id;
    }

    // Each output explicitly selects its unresolved reads. Outputs may only
    // reference already recorded facts/roots, so same-operation forward edges,
    // cycles and cross-forecast references cannot enter the graph.
    internal ImmutableArray<PredictorKnowledgeFactId> Record(string operation, string nodeId, string boundary,
        ImmutableArray<PredictorKnowledgeFactId> allReadFactIds, params PredictorKnowledgeWrite[] outputs)
    {
        RequireText(operation); RequireText(nodeId); RequireText(boundary);
        if (outputs == null || outputs.Length == 0 || outputs.Any(o => o == null) ||
            outputs.Select(o => o.PropertyKey).Distinct(StringComparer.Ordinal).Count() != outputs.Length)
            throw new ArgumentException("PredictorKnowledgeOutputsInvalid");
        var reads = Normalize(allReadFactIds);
        foreach (var id in reads) RequireFact(id);
        var operationId = new PredictorKnowledgeOperationId(ForecastId, _operations.Count + 1);
        var facts = ImmutableArray.CreateBuilder<PredictorKnowledgeFact>(outputs.Length);
        foreach (var output in outputs)
        {
            RequireText(output.PropertyKey);
            if (!Enum.IsDefined(output.Status) || !Enum.IsDefined(output.Origin))
                throw new ArgumentException("PredictorKnowledgeFactInvalid");
            var unresolved = Normalize(output.UnresolvedReadFactIds);
            var roots = Normalize(output.DirectRootIds);
            foreach (var id in unresolved)
            {
                var input = RequireFact(id);
                if (!reads.Contains(id) || input.Status == PredictorKnowledgeStatus.Known)
                    throw new ArgumentException("PredictorKnowledgeUnresolvedReadInvalid");
            }
            foreach (var id in roots) RequireRoot(id);
            var details = output.ProvenDetails.IsDefault ? [] : output.ProvenDetails;
            if (details.Any(d => d == null || string.IsNullOrWhiteSpace(d.Key) || d.Text == null))
                throw new ArgumentException("PredictorKnowledgeDisplayInvalid");
            bool hasCause = !unresolved.IsEmpty || !roots.IsEmpty;
            if (output.Status == PredictorKnowledgeStatus.Known)
            {
                if (hasCause || output.DisplayValue == null && details.IsEmpty ||
                    reads.Any(id => RequireFact(id).Status != PredictorKnowledgeStatus.Known) &&
                        string.IsNullOrWhiteSpace(output.Derivation))
                    throw new ArgumentException("PredictorKnowledgeKnownProofInvalid");
            }
            else
            {
                if (!hasCause) throw new ArgumentException("PredictorKnowledgeUnknownWithoutCause");
                if (output.Status == PredictorKnowledgeStatus.Partial)
                {
                    if (output.DisplayValue == null && details.IsEmpty)
                        throw new ArgumentException("PredictorKnowledgePartialDisplayMissing");
                }
                else if (output.DisplayValue != null || !details.IsEmpty)
                    throw new ArgumentException("PredictorKnowledgeUnknownDisplayInvalid");
            }
            facts.Add(new(new(ForecastId, _facts.Count + facts.Count + 1), output.PropertyKey, nodeId,
                boundary, operationId, output.Status, output.Origin, output.DisplayValue, details,
                output.Derivation, reads, unresolved, roots));
        }
        // Validate every output before changing the ledger; a rejected operation
        // consumes no IDs and cannot leave a partially published write behind.
        var produced = facts.Select(f => f.Id).ToImmutableArray();
        _facts.AddRange(facts);
        _operations.Add(new(operationId, operation, nodeId, boundary, reads, produced));
        return produced;
    }

    internal PredictorKnowledgeSnapshot Snapshot() => new(ForecastId,
        _facts.ToImmutableArray(), _roots.ToImmutableArray(), _operations.ToImmutableArray());

    internal PredictorKnowledgeFact Fact(PredictorKnowledgeFactId id) => RequireFact(id);

    private PredictorKnowledgeFact RequireFact(PredictorKnowledgeFactId id) =>
        id.ForecastId == ForecastId && id.Ordinal > 0 && id.Ordinal <= _facts.Count
            ? _facts[id.Ordinal - 1] : throw new ArgumentException("PredictorKnowledgeFactMissing");
    private void RequireRoot(PredictorKnowledgeRootId id)
    {
        if (id.ForecastId != ForecastId || id.Ordinal <= 0 || id.Ordinal > _roots.Count)
            throw new ArgumentException("PredictorKnowledgeRootMissing");
    }
    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values) =>
        values.IsDefault ? [] : values.Distinct().ToImmutableArray();
    private static void RequireText(string value)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("PredictorKnowledgeTextMissing"); }
}
