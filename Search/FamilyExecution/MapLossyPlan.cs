using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// A proposal is never a map fact. Drawings require a complete same-route witness;
// property extrema remain independent and may come from different complete paths.
internal sealed record MapLossyPredicate(int Act, MapDrawingProgram Program, string Source);
internal sealed record MapLossyProperty(int Scope, int Metric, StandardMapComparison Comparison, int Value, string Source);
internal sealed record MapLossyActPlan(int Act, IReadOnlyList<MapLossyPredicate> Drawings, IReadOnlyList<int> PropertyMetrics);
internal sealed record MapLossyPlan
{
    internal MapLossyPlan(IReadOnlyList<MapLossyPredicate> predicates, IReadOnlyList<string> remainders)
        : this(predicates, [], remainders) { }

    internal MapLossyPlan(IReadOnlyList<MapLossyPredicate> predicates, IReadOnlyList<MapLossyProperty> properties,
        IReadOnlyList<string> remainders)
    {
        Predicates = Array.AsReadOnly(predicates.ToArray());
        Properties = Array.AsReadOnly(properties.ToArray());
        Remainders = Array.AsReadOnly(remainders.ToArray());
        Acts = Array.AsReadOnly(Enumerable.Range(1, 3).Select(act => new MapLossyActPlan(act,
            Array.AsReadOnly(Predicates.Where(p => p.Act == act).ToArray()),
            Array.AsReadOnly(Properties.Where(p => p.Scope == 0 || p.Scope == act)
                .Select(p => p.Metric).Distinct().Order().ToArray())))
            .Where(a => a.Drawings.Count > 0 || a.PropertyMetrics.Count > 0).ToArray());
    }

    internal IReadOnlyList<MapLossyPredicate> Predicates { get; }
    internal IReadOnlyList<MapLossyProperty> Properties { get; }
    internal IReadOnlyList<MapLossyActPlan> Acts { get; }
    internal IReadOnlyList<string> Remainders { get; }
    internal bool Enabled => Predicates.Count > 0 || Properties.Count > 0;
    internal const string Strategy = MapLossyOptions.CurrentStrategy;
    internal const int MaxProgramStates = 512;
    internal string Scope => string.Join(',', Predicates.Select(p => p.Source).Concat(Properties.Select(p => p.Source)));
    internal string Reason => Enabled ? "AppliedToSupportedConditions;OtherConditionsFullFinal" :
        Remainders.Count > 0 ? string.Join(',', Remainders) : "NoSupportedMapConditions";

    internal static MapLossyPlan Create(ExactSearchExecutionRequest request)
    {
        var predicates = new List<MapLossyPredicate>(); var properties = new List<MapLossyProperty>();
        var remainders = new List<string>();
        if (request.RunOptions.MapLossy?.Enabled != true) return new([], ["Disabled"]);
        if (request.ProfileId != RolltheSpire2.Compatibility.RuntimeProfileId.Beta111) return new([], ["Beta111StandardMapsOnly"]);
        if (request.Authority.PlayersCount != 1) return new([], ["SingleplayerOnly"]);
        foreach (var route in request.CompiledSearch.NormalizedQuery.StandardMapRoutes)
        {
            string source = $"Act{route.Act}.Drawing";
            Add(route.Act, route.ToSequenceQuery(), source);
        }
        foreach (var c in request.CompiledSearch.NormalizedQuery.StandardMaps)
        {
            string source = $"Scope{c.Scope}.{c.Metric}";
            if (!c.IsValid) { remainders.Add(source + ":FullFinalOnly"); continue; }
            // These nine enum values are the scalar program's metric ordering.
            // Retain Scope 0 as one sum of three same-seed Act extrema, never as
            // a requirement that every Act separately reaches the total value.
            properties.Add(new(c.Scope, (int)c.Metric, c.Comparison, c.Value, source));
        }
        return new(predicates, properties, remainders);

        void Add(int act, MapRouteSequenceQuery query, string source)
        {
            if (!TryCountableBudget(act, out int budget))
            { remainders.Add(source + ":UnprovenCountableRoomBudget"); return; }
            if (!TryProgram(query, budget, out var program, out string reason))
            { remainders.Add(source + ":" + reason); return; }
            if (!program.AlwaysReject && program.StateCount == 1)
            { remainders.Add(source + ":NoSelectiveRequirement"); return; }
            predicates.Add(new(act, program, source));
        }
    }

    internal static MapLossyExecutionDescription Describe(ExactSearchExecutionRequest request, bool preferGpu, bool float64Available)
    {
        var plan = Create(request);
        bool int64Available = FamilyDeviceProfileFoundation.Initialization.Capture().VulkanCapabilities?.ShaderInt64 != false;
        string backend = !plan.Enabled ? "FullFinal" : preferGpu && float64Available && int64Available ? "Gpu" : "Cpu";
        string reason = !plan.Enabled ? plan.Reason : !preferGpu ? "CpuSelectedOrGpuUnavailable" :
            !float64Available ? "Float64Unavailable" : !int64Available ? "Int64Unavailable" : "GpuSelected";
        return new(request.MapLossy.Enabled, plan.Enabled, backend, plan.Enabled ? Strategy : "FullFinal",
            reason, plan.Scope, plan.Remainders.ToArray());
    }

    // Only the supported Beta111 SP act contexts establish this bound. The fixed
    // first Monster, treasure/replacement row, and final Rest do not participate.
    // The caller's profile/player checks above are required before using it.
    private static bool TryCountableBudget(int act, out int budget)
    {
        budget = 0;
        if (act is < 1 or > 3) return false;
        var context = StandardMapFamily.Context(act, 0);
        budget = Beta111StandardMapGenerator.GetNumberOfRooms(context.Act, false) - 3;
        return true;
    }

    internal static bool TryProgram(MapRouteSequenceQuery query, int provenCountableBudget,
        out MapDrawingProgram program, out string reason)
    {
        program = null!; reason = "";
        if (query.AdditionalCounts.Count > 0)
        { reason = "AdditionalCountsFullFinalOnly"; return false; }
        try { program = MapDrawingProgram.Compile(query, provenCountableBudget); }
        catch (NotSupportedException)
        { reason = "DrawingStateTableCapacityExceeded"; return false; }
        // Physical representation limits are never proofs of logical mismatch.
        // With the proven standard SP budget <=12 the maximum is 432 states.
        if (program.StateCount > MaxProgramStates)
        { program = null!; reason = "DrawingProgramExceeds512States"; return false; }
        return true;
    }
}

internal sealed record MapLossyExecutionDescription(bool Requested, bool Applied, string Backend,
    string Strategy, string Reason, string Scope, IReadOnlyList<string> FullFinalRemainders);
