using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Converts current compiled predicates to the legacy filter DTO consumed by
/// selectivity/reference code and old input compilation; not an execution backend.
/// </summary>
public static class LegacySearchCriteriaAdapter
{
    public static LegacySearchCriteriaProjection Project(CompiledSearch compiled)
    {
        ExactSearchEvaluationProjectionResult projected = CompiledSearchEvaluationProjector.Project(compiled);
        if (compiled.NormalizedQuery.TransformationAggregate is not null)
            return new LegacySearchCriteriaProjection(projected.Evaluation.ToLegacyFilter(), ProjectionFidelity.Unsupported,
                ["TransformationAggregateCannotLowerToLegacyLeafFilter"]);
        return new LegacySearchCriteriaProjection(
            projected.Evaluation.ToLegacyFilter(),
            projected.Fidelity,
            projected.Diagnostics);
    }
}
