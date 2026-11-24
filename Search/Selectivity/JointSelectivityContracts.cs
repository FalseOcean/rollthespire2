using System.Text;

namespace RolltheSpire2.Search.Selectivity;

internal enum JointSelectivityCombinationMethod : byte
{
    Unknown = 0,
    SingleAuthorityTerm = 1,
    IndependentProduct = 2,
    WithoutReplacementJoint = 3,
    ConditionalChain = 4,
    FiniteMixture = 5,
    ConditionalEligibility = 6,
    RouteConditional = 7,
    ExactImpossible = 8,
    PartialPricing = 9
}

internal enum SelectivityDependencyNodeKind : byte
{
    QueryRoot = 0,
    ObservableCondition = 1,
    LatentParent = 2,
    EligibilityState = 3,
    RouteState = 4,
    FinitePool = 5
}

internal enum SelectivityDependencyEdgeKind : byte
{
    Independent = 0,
    ConditionalOn = 1,
    SharedParent = 2,
    WithoutReplacement = 3,
    SharedContinuation = 4,
    RouteDependent = 5,
    Unknown = 6,
    AssumedIndependent = 7
}

internal sealed record SelectivityDependencyNode(
    string Id,
    SelectivityDependencyNodeKind Kind,
    string Label,
    double? Probability,
    string AuthoritySource,
    SearchSelectivityDependencyClass DependencyClass,
    IReadOnlyList<string> Assumptions);

internal sealed record SelectivityDependencyEdge(
    string FromId,
    string ToId,
    SelectivityDependencyEdgeKind Kind,
    string AuthoritySource,
    string Notes);

internal sealed record SelectivityDependencyGraph(
    IReadOnlyList<SelectivityDependencyNode> Nodes,
    IReadOnlyList<SelectivityDependencyEdge> Edges)
{
    public static SelectivityDependencyGraph Empty { get; } = new(
        Array.Empty<SelectivityDependencyNode>(),
        Array.Empty<SelectivityDependencyEdge>());
}

internal sealed record JointSelectivityComponent(
    string Id,
    string Condition,
    string Parent,
    double? Probability,
    string AuthoritySource,
    JointSelectivityCombinationMethod Method,
    SearchSelectivityDependencyClass DependencyClass,
    bool EligibilityKnown,
    bool Eligible,
    string BranchReason,
    IReadOnlyList<string> Assumptions);

internal sealed record JointSelectivityBranchTrace(
    string BranchId,
    string ParentLabel,
    double PriorProbability,
    bool PriorExact,
    double? ConditionalProbability,
    bool BranchPruned,
    string BranchReason,
    IReadOnlyList<JointSelectivityComponent> Components,
    IReadOnlyList<string> Assumptions)
{
    public double? WeightedProbability =>
        ConditionalProbability.HasValue ? PriorProbability * ConditionalProbability.Value : null;
}

internal sealed record JointSelectivityResult(
    double? Probability,
    double? EstimatedSurvivalRate,
    bool Priced,
    bool PartiallyPriced,
    bool ExactlyImpossible,
    SearchSelectivityConfidence Confidence,
    JointSelectivityCombinationMethod Method,
    string DependencyCoverage,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<JointSelectivityComponent> KnownComponents,
    IReadOnlyList<string> UnknownComponents,
    IReadOnlyList<JointSelectivityBranchTrace> Branches,
    SelectivityDependencyGraph DependencyGraph,
    string EvidenceCode,
    string Notes,
    string Derivation)
{
    /// <summary>
    /// S3 developer-only provenance for the Canonical Probability semantic projection.
    /// This does not participate in numerical pricing or Planner ranking.
    /// </summary>
    public IReadOnlyList<string> ProbabilitySemanticDiagnostics { get; init; } = Array.Empty<string>();

    public bool JointlyPriced => Priced && Probability.HasValue;
    public double PlanningUpperBoundRate => JointlyPriced ? Math.Clamp(Probability!.Value, 0d, 1d) : 1d;

    public static JointSelectivityResult Exact(
        double probability,
        JointSelectivityCombinationMethod method,
        string evidenceCode,
        string notes,
        string derivation,
        SelectivityDependencyGraph? graph = null,
        IReadOnlyList<JointSelectivityComponent>? components = null,
        IReadOnlyList<JointSelectivityBranchTrace>? branches = null,
        IReadOnlyList<string>? assumptions = null,
        string dependencyCoverage = "Complete")
    {
        double p = Clamp(probability);
        return new JointSelectivityResult(
            p,
            p,
            true,
            false,
            p == 0d,
            SearchSelectivityConfidence.High,
            p == 0d ? JointSelectivityCombinationMethod.ExactImpossible : method,
            dependencyCoverage,
            assumptions ?? Array.Empty<string>(),
            components ?? Array.Empty<JointSelectivityComponent>(),
            Array.Empty<string>(),
            branches ?? Array.Empty<JointSelectivityBranchTrace>(),
            graph ?? SelectivityDependencyGraph.Empty,
            evidenceCode,
            notes,
            derivation);
    }

    public static JointSelectivityResult Partial(
        string evidenceCode,
        string notes,
        string derivation,
        IReadOnlyList<JointSelectivityComponent> knownComponents,
        IReadOnlyList<string> unknownComponents,
        SelectivityDependencyGraph? graph = null,
        IReadOnlyList<JointSelectivityBranchTrace>? branches = null,
        IReadOnlyList<string>? assumptions = null,
        SearchSelectivityConfidence confidence = SearchSelectivityConfidence.Low,
        string dependencyCoverage = "Partial") =>
        new(
            null,
            null,
            false,
            knownComponents.Count != 0,
            false,
            confidence,
            JointSelectivityCombinationMethod.PartialPricing,
            dependencyCoverage,
            assumptions ?? Array.Empty<string>(),
            knownComponents,
            unknownComponents,
            branches ?? Array.Empty<JointSelectivityBranchTrace>(),
            graph ?? SelectivityDependencyGraph.Empty,
            evidenceCode,
            notes,
            derivation);

    public static JointSelectivityResult Unpriced(
        string evidenceCode,
        string notes,
        string derivation,
        IReadOnlyList<string>? unknownComponents = null,
        SelectivityDependencyGraph? graph = null,
        IReadOnlyList<string>? assumptions = null) =>
        new(
            null,
            null,
            false,
            false,
            false,
            SearchSelectivityConfidence.Unknown,
            JointSelectivityCombinationMethod.Unknown,
            "Unknown",
            assumptions ?? Array.Empty<string>(),
            Array.Empty<JointSelectivityComponent>(),
            unknownComponents ?? Array.Empty<string>(),
            Array.Empty<JointSelectivityBranchTrace>(),
            graph ?? SelectivityDependencyGraph.Empty,
            evidenceCode,
            notes,
            derivation);

    public string ToHumanReadable()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Joint Selectivity Derivation");
        sb.AppendLine($"Evidence: {EvidenceCode}");
        sb.AppendLine($"Method: {Method}");
        sb.AppendLine($"Priced: {Priced}");
        sb.AppendLine($"PartiallyPriced: {PartiallyPriced}");
        sb.AppendLine($"ExactlyImpossible: {ExactlyImpossible}");
        sb.AppendLine($"Probability: {(Probability.HasValue ? Probability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : "Unknown")}");
        foreach (string diagnostic in ProbabilitySemanticDiagnostics)
            sb.AppendLine("Semantic: " + diagnostic);
        var branchComponentIds = Branches.SelectMany(branch => branch.Components).Select(component => component.Id).ToHashSet(StringComparer.Ordinal);
        foreach (JointSelectivityComponent component in KnownComponents.Where(component => !branchComponentIds.Contains(component.Id)))
        {
            sb.AppendLine($"Known Component: {component.Condition}");
            sb.AppendLine($"  Authority = {component.AuthoritySource}");
            sb.AppendLine($"  Parent = {component.Parent}");
            sb.AppendLine($"  Method = {component.Method}");
            sb.AppendLine($"  Probability = {(component.Probability.HasValue ? component.Probability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : "Unknown")}");
            sb.AppendLine($"  Eligibility = {(component.EligibilityKnown ? component.Eligible.ToString() : "Unknown")}");
            sb.AppendLine($"  DependencyClass = {component.DependencyClass}");
            if (!string.IsNullOrWhiteSpace(component.BranchReason)) sb.AppendLine($"  BranchReason = {component.BranchReason}");
        }
        foreach (JointSelectivityBranchTrace branch in Branches)
        {
            sb.AppendLine($"Branch: {branch.ParentLabel}");
            sb.AppendLine($"  Prior = {branch.PriorProbability.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (JointSelectivityComponent component in branch.Components)
            {
                sb.AppendLine($"  Condition: {component.Condition}");
                sb.AppendLine($"    Authority = {component.AuthoritySource}");
                sb.AppendLine($"    Parent = {component.Parent}");
                sb.AppendLine($"    Method = {component.Method}");
                sb.AppendLine($"    Probability = {(component.Probability.HasValue ? component.Probability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : "Unknown")}");
                sb.AppendLine($"    Eligibility = {(component.EligibilityKnown ? component.Eligible.ToString() : "Unknown")}");
                sb.AppendLine($"    DependencyClass = {component.DependencyClass}");
                if (!string.IsNullOrWhiteSpace(component.BranchReason)) sb.AppendLine($"    BranchReason = {component.BranchReason}");
            }
            sb.AppendLine($"  BranchProbability = {(branch.ConditionalProbability.HasValue ? branch.ConditionalProbability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : "Unknown")}");
            sb.AppendLine($"  BranchPruned = {branch.BranchPruned}");
            if (!string.IsNullOrWhiteSpace(branch.BranchReason)) sb.AppendLine($"  Reason = {branch.BranchReason}");
        }
        if (UnknownComponents.Count != 0)
            sb.AppendLine("Unpriced: " + string.Join(" | ", UnknownComponents));
        sb.AppendLine("Final: " + (Probability.HasValue ? "P(Query)=" + Probability.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : "P(Query)=Unknown"));
        if (!string.IsNullOrWhiteSpace(Derivation)) sb.AppendLine(Derivation);
        return sb.ToString();
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Joint probability must be finite.");
        return Math.Clamp(value, 0d, 1d);
    }
}
