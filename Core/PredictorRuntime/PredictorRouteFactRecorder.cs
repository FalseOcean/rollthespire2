using System.Collections.Immutable;
using System.Globalization;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRouteFactPhase { Before, After, Results }
internal sealed record PredictorRouteRecordedFrame(PredictorKnowledgeSnapshot Snapshot,
    ImmutableDictionary<string, PredictorKnowledgeFactId> Before,
    ImmutableDictionary<string, PredictorKnowledgeFactId> After,
    ImmutableDictionary<string, PredictorKnowledgeFactId> Results)
{
    internal PredictorNodeDiagnostics Diagnostics => new(Snapshot,
        Before.Values.OrderBy(id => id.Ordinal).ToImmutableArray(),
        After.Values.OrderBy(id => id.Ordinal).ToImmutableArray(),
        Results.Values.OrderBy(id => id.Ordinal).ToImmutableArray());
}

// One evidence owner per forecast; each calculation has its own cause cache.
// Branches publish immutable facts/IDs and diagnostic frames, never mutable
// input knowledge. The evidence owner never retains/sweeps a state or witness.
internal sealed class PredictorRouteFactRecorder
{
    private sealed class NodeFrame
    {
        internal readonly Dictionary<string, PredictorKnowledgeFactId> Before = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, PredictorKnowledgeFactId> After = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, PredictorKnowledgeFactId> Results = new(StringComparer.Ordinal);
        internal Dictionary<string, PredictorKnowledgeFactId> For(PredictorRouteFactPhase phase) => phase switch
        { PredictorRouteFactPhase.Before => Before, PredictorRouteFactPhase.After => After,
            PredictorRouteFactPhase.Results => Results, _ => throw new ArgumentOutOfRangeException(nameof(phase)) };
    }
    private sealed class CauseEntry(PredictorCombatPendingCause cause)
    {
        internal readonly PredictorCombatPendingCause Origin = cause;
        internal readonly List<string> Properties = [];
        internal readonly List<string> Parents = [];
        internal readonly List<string> SourceEvidence = [];
        internal PredictorKnowledgeRootId? Root;
        internal ImmutableArray<PredictorKnowledgeFactId> RecordedParents = [];
        internal ImmutableArray<string> RecordedProperties = [];
        internal bool RecordedAsOrigin;
        internal int RecordedEvidenceCount;
        internal ImmutableArray<PredictorKnowledgeFactId> Facts = [];

        internal CauseEntry Copy()
        {
            var copy = new CauseEntry(Origin) { Root = Root, RecordedParents = RecordedParents,
                RecordedProperties = RecordedProperties, RecordedAsOrigin = RecordedAsOrigin,
                RecordedEvidenceCount = RecordedEvidenceCount, Facts = Facts };
            copy.Properties.AddRange(Properties);
            copy.Parents.AddRange(Parents);
            copy.SourceEvidence.AddRange(SourceEvidence);
            return copy;
        }
    }

    // Frames are output indexes only (read by FreezeFrames), not values used by
    // predicates. A shared trace preserves cross-branch FactId references.
    private sealed class ForecastEvidence
    {
        internal readonly PredictorKnowledgeTrace Trace = new();
        internal readonly Dictionary<string, NodeFrame> Frames = new(StringComparer.Ordinal);
        internal readonly Dictionary<(string Node, string Identity), ImmutableDictionary<string, ImmutableArray<PredictorKnowledgeFactId>>> EventReads = [];
    }
    private readonly ForecastEvidence _evidence;
    private readonly Dictionary<string, CauseEntry> _causes = new(StringComparer.Ordinal);
    internal PredictorRouteFactRecorder() => _evidence = new();
    private PredictorRouteFactRecorder(PredictorRouteFactRecorder source)
    {
        _evidence = source._evidence;
        foreach (var pair in source._causes) _causes.Add(pair.Key, pair.Value.Copy());
    }
    internal PredictorRouteFactRecorder Fork() => new(this);
    internal void PublishEventReads(string node, string identity,
        ImmutableDictionary<string, ImmutableArray<PredictorKnowledgeFactId>> groups) =>
        _evidence.EventReads[(node, identity)] = groups;
    internal ImmutableArray<PredictorKnowledgeFactId> EventResultReads(string node, string? identity, string field) =>
        identity != null && _evidence.EventReads.TryGetValue((node, identity), out var groups)
            ? groups.GetValueOrDefault(field, groups.GetValueOrDefault("stage", [])) : [];
    internal Guid ForecastId => _evidence.Trace.ForecastId;

    // Nonempty unresolved causes/reads mask value before calling a formatter.
    // A proven invariant instead supplies no unresolved dependencies, retains
    // its complete AllRead list, and supplies the model's derivation.
    internal PredictorKnowledgeFactId RecordValue<T>(string propertyKey, string nodeId,
        PredictorRouteFactPhase phase, T value, ImmutableArray<PredictorCombatPendingCause> causes = default,
        PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived, string operation = "property",
        ImmutableArray<PredictorKnowledgeFactId> allReadFactIds = default,
        ImmutableArray<PredictorKnowledgeFactId> unresolvedReadFactIds = default,
        string? derivation = null, Func<T, string>? safeFormatter = null)
    {
        var dependencies = Import(causes).AddRange(Empty(unresolvedReadFactIds)).Distinct().ToImmutableArray();
        var status = dependencies.IsEmpty ? PredictorKnowledgeStatus.Known : PredictorKnowledgeStatus.Unknown;
        string? display = dependencies.IsEmpty ? safeFormatter == null ? Scalar(value) : safeFormatter(value) : null;
        return Record(propertyKey, nodeId, phase, status, display, [], dependencies, origin,
            operation, allReadFactIds, derivation);
    }

    internal PredictorKnowledgeFactId RecordResult<T>(string propertyKey, string nodeId, T value,
        ImmutableArray<PredictorCombatPendingCause> causes = default,
        PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived, string operation = "result",
        ImmutableArray<PredictorKnowledgeFactId> allReadFactIds = default,
        ImmutableArray<PredictorKnowledgeFactId> unresolvedReadFactIds = default,
        string? derivation = null, Func<T, string>? safeFormatter = null) =>
        RecordValue(propertyKey, nodeId, PredictorRouteFactPhase.Results, value, causes, origin,
            operation, allReadFactIds, unresolvedReadFactIds, derivation, safeFormatter);

    // The supplied partial display/details must themselves be established by
    // the owning model (e.g. a guaranteed subset or conservative enclosure).
    internal PredictorKnowledgeFactId RecordPartial(string propertyKey, string nodeId,
        PredictorRouteFactPhase phase, string safeDisplayValue,
        ImmutableArray<PredictorCombatPendingCause> causes = default,
        ImmutableArray<PredictorKnowledgeDetail> provenDetails = default,
        PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived, string operation = "partial-property",
        ImmutableArray<PredictorKnowledgeFactId> allReadFactIds = default,
        ImmutableArray<PredictorKnowledgeFactId> unresolvedReadFactIds = default, string? derivation = null) =>
        Record(propertyKey, nodeId, phase, PredictorKnowledgeStatus.Partial, safeDisplayValue, Empty(provenDetails),
            Import(causes).AddRange(Empty(unresolvedReadFactIds)).Distinct().ToImmutableArray(), origin,
            operation, allReadFactIds, derivation);

    internal PredictorKnowledgeFactId RecordUnknown(string propertyKey, string nodeId,
        PredictorRouteFactPhase phase, ImmutableArray<PredictorCombatPendingCause> causes = default,
        PredictorKnowledgeStatus status = PredictorKnowledgeStatus.Unknown,
        PredictorKnowledgeOrigin origin = PredictorKnowledgeOrigin.Derived, string operation = "unresolved-property",
        ImmutableArray<PredictorKnowledgeFactId> allReadFactIds = default,
        ImmutableArray<PredictorKnowledgeFactId> unresolvedReadFactIds = default)
    {
        if (status is not (PredictorKnowledgeStatus.Unknown or PredictorKnowledgeStatus.NotObserved))
            throw new ArgumentException("PredictorRouteFactUnknownStatusInvalid", nameof(status));
        return Record(propertyKey, nodeId, phase, status, null, [],
            Import(causes).AddRange(Empty(unresolvedReadFactIds)).Distinct().ToImmutableArray(), origin,
            operation, allReadFactIds, null);
    }

    private PredictorKnowledgeFactId Record(string propertyKey, string nodeId, PredictorRouteFactPhase phase,
        PredictorKnowledgeStatus status, string? display, ImmutableArray<PredictorKnowledgeDetail> details,
        ImmutableArray<PredictorKnowledgeFactId> dependencies, PredictorKnowledgeOrigin origin,
        string operation, ImmutableArray<PredictorKnowledgeFactId> allReads, string? derivation)
    {
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        var reads = Empty(allReads).AddRange(dependencies).Distinct().ToImmutableArray();
        var id = _evidence.Trace.Record(operation, nodeId, phase.ToString(), reads,
            new PredictorKnowledgeWrite(propertyKey, status, origin, display,
                UnresolvedReadFactIds: dependencies, Derivation: derivation, ProvenDetails: details))[0];
        if (!_evidence.Frames.TryGetValue(nodeId, out var frame)) _evidence.Frames[nodeId] = frame = new();
        frame.For(phase)[propertyKey] = id;
        return id;
    }

    internal PredictorKnowledgeSnapshot Snapshot() => _evidence.Trace.Snapshot();
    internal PredictorKnowledgeFact Fact(PredictorKnowledgeFactId id) => _evidence.Trace.Fact(id);

    internal ImmutableDictionary<string, PredictorRouteRecordedFrame> FreezeFrames()
    {
        var snapshot = Snapshot();
        return _evidence.Frames.ToImmutableDictionary(pair => pair.Key, pair => new PredictorRouteRecordedFrame(snapshot,
            pair.Value.Before.ToImmutableDictionary(StringComparer.Ordinal),
            pair.Value.After.ToImmutableDictionary(StringComparer.Ordinal),
            pair.Value.Results.ToImmutableDictionary(StringComparer.Ordinal)), StringComparer.Ordinal);
    }

    internal ImmutableDictionary<string, PredictorNodeDiagnostics> Freeze() =>
        FreezeFrames().ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Diagnostics, StringComparer.Ordinal);

    private ImmutableArray<PredictorKnowledgeFactId> Import(ImmutableArray<PredictorCombatPendingCause> causes)
    {
        if (causes.IsDefaultOrEmpty) return [];
        // Gather every immutable source version once before writing any facts.
        // Distinct versions of one identity can contribute additional fields
        // or parents, including to an ancestor shared by multiple top-level causes.
        var bound = new Dictionary<PredictorCombatPendingCause, PredictorCombatPendingCause>(ReferenceEqualityComparer.Instance);
        PredictorCombatPendingCause BindOnce(PredictorCombatPendingCause source)
        {
            if (!bound.TryGetValue(source, out var value)) bound.Add(source, value = Bound(source));
            return value;
        }
        var seen = new HashSet<PredictorCombatPendingCause>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PredictorCombatPendingCause>(causes.Reverse());
        while (pending.TryPop(out var input))
        {
            if (!seen.Add(input)) continue;
            var cause = BindOnce(input);
            if (!_causes.TryGetValue(cause.Identity, out var entry))
                _causes[cause.Identity] = entry = new(cause);
            else if (entry.Origin.NodeId != cause.NodeId || entry.Origin.ChoiceId != cause.ChoiceId ||
                entry.Origin.ReasonKind != cause.ReasonKind || entry.Origin.Code != cause.Code ||
                entry.Origin.Description != cause.Description)
                throw new ArgumentException("PredictorRouteCauseOriginConflict");
            foreach (string property in Empty(cause.RequiredPropertyKeys))
                if (!entry.Properties.Contains(property, StringComparer.Ordinal)) entry.Properties.Add(property);
            foreach (string evidence in cause.SourceEvidence.Split('\n'))
                if (!entry.SourceEvidence.Contains(evidence, StringComparer.Ordinal)) entry.SourceEvidence.Add(evidence);
            foreach (var parent in Empty(cause.Parents))
            {
                string identity = BindOnce(parent).Identity;
                if (!entry.Parents.Contains(identity, StringComparer.Ordinal)) entry.Parents.Add(identity);
            }
            foreach (var parent in Empty(cause.Parents).Reverse()) pending.Push(parent);
        }

        // Facts are memoized only after all contributions in this import have
        // been gathered. Each recorder/fork can learn more evidence later.
        var recorded = new Dictionary<string, ImmutableArray<PredictorKnowledgeFactId>>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        return causes.Select(input => BindOnce(input).Identity).Distinct(StringComparer.Ordinal)
            .SelectMany(identity => Import(identity, active, recorded)).Distinct().ToImmutableArray();
    }

    private ImmutableArray<PredictorKnowledgeFactId> Import(string identity, HashSet<string> active,
        Dictionary<string, ImmutableArray<PredictorKnowledgeFactId>> recorded)
    {
        if (recorded.TryGetValue(identity, out var known)) return known;
        if (!active.Add(identity)) throw new ArgumentException("PredictorRouteCauseCycle");
        try
        {
            var entry = _causes[identity];
            var cause = entry.Origin;
            var parentFacts = entry.Parents.SelectMany(parent => Import(parent, active, recorded))
                .Distinct().ToImmutableArray();
            // A classified gap/guard remains its own origin even with parents.
            // Only explicit property dependency wrappers pass roots through.
            bool isOrigin = PredictorRouteCauseEvidence.IsOriginalCause(cause);
            if (!isOrigin && parentFacts.IsEmpty)
                throw new ArgumentException("PredictorDerivedDependencyOriginMissing");
            if (isOrigin && entry.Root == null) entry.Root = RegisterRoot(entry.Origin);
            var properties = entry.Properties.Count == 0
                ? ImmutableArray.Create("dependency." + cause.Code) : entry.Properties.ToImmutableArray();
            if (!entry.Facts.IsEmpty && entry.RecordedParents.SequenceEqual(parentFacts) &&
                entry.RecordedProperties.SequenceEqual(properties) && entry.RecordedAsOrigin == isOrigin &&
                entry.RecordedEvidenceCount == entry.SourceEvidence.Count)
                return recorded[identity] = entry.Facts;
            var roots = isOrigin ? ImmutableArray.Create(entry.Root!.Value) : [];
            var status = cause.ReasonKind == PredictorPendingReasonKind.MissingObservation
                ? PredictorKnowledgeStatus.NotObserved : PredictorKnowledgeStatus.Unknown;
            string evidence = string.Join("\n", entry.SourceEvidence);
            entry.Facts = _evidence.Trace.Record("dependency:" + cause.Code, cause.NodeId ?? "unobserved-origin", "dependency",
                parentFacts, properties.Select(property => new PredictorKnowledgeWrite(property, status,
                    PredictorKnowledgeOrigin.Derived, UnresolvedReadFactIds: parentFacts, DirectRootIds: roots,
                    Derivation: evidence)).ToArray());
            entry.RecordedParents = parentFacts; entry.RecordedProperties = properties; entry.RecordedAsOrigin = isOrigin;
            entry.RecordedEvidenceCount = entry.SourceEvidence.Count;
            return recorded[identity] = entry.Facts;
        }
        finally { active.Remove(identity); }
    }

    private PredictorKnowledgeRootId RegisterRoot(PredictorCombatPendingCause cause)
    {
        if (!Enum.TryParse<PredictorKnowledgeRootKind>(cause.ReasonKind.ToString(), out var kind) || !Enum.IsDefined(kind))
            throw new ArgumentException("PredictorRouteCauseKindNotRepresented");
        // A repair identity stays fixed when a later merge adds affected
        // properties. Those properties remain individual facts in the graph.
        // This is one choice/observation to resolve, not one new root per field.
        ImmutableArray<PredictorKnowledgeRequiredInput> required = cause.ReasonKind switch
        {
            PredictorPendingReasonKind.UserChoice => [new(cause.ChoiceId ?? throw new ArgumentException("PredictorRouteChoiceIdentityMissing"),
                cause.Code + " · " + (cause.Choice?.Description ?? cause.Description))],
            PredictorPendingReasonKind.MissingObservation => [new("observation:" + cause.Identity, cause.Code + " · " + cause.Description)],
            _ => []
        };
        return _evidence.Trace.AddRoot(cause.Identity, cause.NodeId ?? "unobserved-origin", cause.ChoiceId,
            kind, cause.Description, required);
    }

    private static PredictorCombatPendingCause Bound(PredictorCombatPendingCause cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        // Do not reclassify a fully bound ExecutionFault from its message/code.
        return string.IsNullOrWhiteSpace(cause.Identity) ? PredictorRouteCauseEvidence.Bind(cause) : cause;
    }
    private static ImmutableArray<T> Empty<T>(ImmutableArray<T> array) => array.IsDefault ? [] : array;
    private static string Scalar<T>(T value) => value switch
    {
        null => "∅", string text => text, bool flag => flag.ToString(), char character => character.ToString(),
        Enum label => label.ToString(), IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("PredictorRouteFactSafeFormatterRequired")
    };
}
