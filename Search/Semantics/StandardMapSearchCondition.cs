namespace RolltheSpire2.Search.Semantics;

public enum StandardMapMetric
{
    GuaranteedMonster, GuaranteedElite, GuaranteedRest, GuaranteedUnknown,
    ReachableMaxMonster, ReachableMaxElite, ReachableMaxRest, ReachableMaxUnknown,
    ForcedMonsterPrefix
}
public enum StandardMapComparison { AtLeast, AtMost }

// Scope 0 is the sum of three same-seed Act route maxima. Scope 1..3 is one Act.
// RouteObjective preserves authoring identity; properties are independent scalar facts,
// never a claim that multiple extrema share one path.
public sealed record StandardMapSearchCondition(int Scope, StandardMapMetric Metric,
    StandardMapComparison Comparison, int Value, bool RouteObjective = false)
{
    public bool IsValid => Scope is >= 0 and <= 3 && Value >= 0 &&
        Enum.IsDefined(Metric) && Enum.IsDefined(Comparison) &&
        (!RouteObjective || Comparison == StandardMapComparison.AtLeast && Metric is
            StandardMapMetric.ReachableMaxElite or StandardMapMetric.ReachableMaxRest or StandardMapMetric.ReachableMaxUnknown) &&
        (Scope != 0 || RouteObjective);
}
