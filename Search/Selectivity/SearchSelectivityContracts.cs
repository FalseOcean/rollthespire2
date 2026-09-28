using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Pricing authority is intentionally separate from execution support. A predicate
/// may be CPU-only/ExactOnly and still have an exact authority-derived probability.
/// </summary>
internal enum SearchSelectivityPricingClass : byte
{
    Unpriced = 0,
    ExactPriced = 1,
    ModeledPriced = 2,
    HeuristicPriced = 3
}

internal enum SearchSelectivityConfidence : byte
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal enum SearchSelectivityMethod : byte
{
    Unknown = 0,
    AuthorityPoolMembership = 1,
    WithoutReplacement = 2,
    ConditionalChain = 3,
    StructuralAssignment = 4,
    RuntimeObservedConditional = 5,
    RuntimeObservedJoint = 6,
    NamedStreamIndependenceModel = 7
}

internal enum SearchSelectivityCoverage : byte
{
    None = 0,
    ExactRequestedPredicate = 1,
    ExactRequestedConjunction = 2,
    PartialRequestedConjunction = 3,
    StageApproximation = 4
}

internal enum SearchSelectivityDependencyClass : byte
{
    ProvenIndependent = 1,
    StructuralDependence = 2,
    SharedContinuation = 3,
    RouteDependent = 4,
    UnknownDependence = 5,
    /// <summary>
    /// Current Probability Design policy: no registered structural/conditional
    /// dependency exists for this cross-domain relation, so product composition
    /// is allowed pending a future RNG ownership audit.
    /// </summary>
    AssumedIndependent = 6
}

internal enum SearchSelectivityDomain : byte
{
    Neow = 1,
    Relic = 2,
    AncientOption = 3,
    WorldEvent = 4,
    CombatReward = 5
}


internal enum ProbabilityExplanationKind : byte
{
    NecessaryImplicitEnabler = 1
}

/// <summary>
/// Narrow player-facing explanation evidence emitted only by Probability authority.
/// It never mutates SearchQuery and is not a semantic/capability/planner contract.
/// Current product scope supports NecessaryImplicitEnabler for Prayer Wheel → Combat Reward.
/// </summary>
internal sealed record ProbabilityExplanation(
    ProbabilityExplanationKind Kind,
    ModelKey ModelKey,
    SearchSelectivityDomain TargetDomain);

/// <summary>
/// Shared P5 estimate contract. Probability is populated only when the current
/// authority supports a direct probability statement. EstimatedSurvivalRate may
/// additionally contain modeled/observed survival. Unpriced never fabricates 1.0.
/// </summary>
internal sealed record SearchSelectivityEstimate(
    double? Probability,
    double? EstimatedSurvivalRate,
    SearchSelectivityConfidence Confidence,
    SearchSelectivityMethod Method,
    SearchSelectivityCoverage Coverage,
    SearchSelectivityDependencyClass DependencyClass,
    SearchSelectivityPricingClass PricingClass,
    IReadOnlyList<string> Assumptions,
    string EvidenceCode,
    string Notes,
    IReadOnlyList<SearchSelectivityDomain> IndependentOfDomains,
    IReadOnlyList<SearchSelectivityDomain> ConditionedOnDomains)
{
    public IReadOnlyList<ProbabilityExplanation> Explanations { get; init; } = Array.Empty<ProbabilityExplanation>();

    public bool IsPriced =>
        PricingClass != SearchSelectivityPricingClass.Unpriced &&
        EstimatedSurvivalRate.HasValue;

    /// <summary>
    /// Candidate-count upper bound used only by the cost planner. This is not a
    /// probability and must never be surfaced as rarity/ETA evidence.
    /// </summary>
    public double PlanningUpperBoundRate =>
        Math.Clamp(EstimatedSurvivalRate ?? 1d, 0d, 1d);

    public static SearchSelectivityEstimate Exact(
        double probability,
        SearchSelectivityMethod method,
        SearchSelectivityCoverage coverage,
        SearchSelectivityDependencyClass dependencyClass,
        string evidenceCode,
        string notes,
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<SearchSelectivityDomain>? independentOfDomains = null,
        IReadOnlyList<SearchSelectivityDomain>? conditionedOnDomains = null) =>
        new(
            ClampProbability(probability),
            ClampProbability(probability),
            SearchSelectivityConfidence.High,
            method,
            coverage,
            dependencyClass,
            SearchSelectivityPricingClass.ExactPriced,
            assumptions ?? Array.Empty<string>(),
            evidenceCode,
            notes,
            independentOfDomains ?? Array.Empty<SearchSelectivityDomain>(),
            conditionedOnDomains ?? Array.Empty<SearchSelectivityDomain>());

    public static SearchSelectivityEstimate Modeled(
        double estimatedSurvivalRate,
        SearchSelectivityConfidence confidence,
        SearchSelectivityMethod method,
        SearchSelectivityCoverage coverage,
        SearchSelectivityDependencyClass dependencyClass,
        string evidenceCode,
        string notes,
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<SearchSelectivityDomain>? independentOfDomains = null,
        IReadOnlyList<SearchSelectivityDomain>? conditionedOnDomains = null) =>
        new(
            null,
            ClampProbability(estimatedSurvivalRate),
            confidence,
            method,
            coverage,
            dependencyClass,
            SearchSelectivityPricingClass.ModeledPriced,
            assumptions ?? Array.Empty<string>(),
            evidenceCode,
            notes,
            independentOfDomains ?? Array.Empty<SearchSelectivityDomain>(),
            conditionedOnDomains ?? Array.Empty<SearchSelectivityDomain>());

    public static SearchSelectivityEstimate Heuristic(
        double estimatedSurvivalRate,
        SearchSelectivityConfidence confidence,
        SearchSelectivityMethod method,
        SearchSelectivityCoverage coverage,
        SearchSelectivityDependencyClass dependencyClass,
        string evidenceCode,
        string notes,
        IReadOnlyList<string>? assumptions = null,
        IReadOnlyList<SearchSelectivityDomain>? independentOfDomains = null,
        IReadOnlyList<SearchSelectivityDomain>? conditionedOnDomains = null) =>
        new(
            null,
            ClampProbability(estimatedSurvivalRate),
            confidence,
            method,
            coverage,
            dependencyClass,
            SearchSelectivityPricingClass.HeuristicPriced,
            assumptions ?? Array.Empty<string>(),
            evidenceCode,
            notes,
            independentOfDomains ?? Array.Empty<SearchSelectivityDomain>(),
            conditionedOnDomains ?? Array.Empty<SearchSelectivityDomain>());

    public static SearchSelectivityEstimate Unpriced(
        string evidenceCode,
        string notes,
        SearchSelectivityConfidence confidence = SearchSelectivityConfidence.Unknown,
        SearchSelectivityMethod method = SearchSelectivityMethod.Unknown,
        SearchSelectivityCoverage coverage = SearchSelectivityCoverage.None,
        SearchSelectivityDependencyClass dependencyClass = SearchSelectivityDependencyClass.UnknownDependence,
        IReadOnlyList<string>? assumptions = null) =>
        new(
            null,
            null,
            confidence,
            method,
            coverage,
            dependencyClass,
            SearchSelectivityPricingClass.Unpriced,
            assumptions ?? Array.Empty<string>(),
            evidenceCode,
            notes,
            Array.Empty<SearchSelectivityDomain>(),
            Array.Empty<SearchSelectivityDomain>());

    private static double ClampProbability(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Selectivity probability must be finite.");
        return Math.Clamp(value, 0d, 1d);
    }
}

internal readonly record struct SearchSelectivityCombinationResult(
    double PlanningSurvivalRate,
    bool JointlyPriced,
    string Reason)
{
    public static SearchSelectivityCombinationResult Unpriced(string reason) => new(1d, false, reason);
}
