using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

[Flags]
internal enum FilterInputModes { None = 0, Dense = 1, CompactAbi1 = 2, All = Dense | CompactAbi1 }

internal sealed record FilterPreparationIssue(string Stage, string Cause, string Detail = "");
internal sealed record FilterObligation(string Domain, int? PlayerSlot = null, int? ThroughOrdinal = null);
internal sealed record PartyChildCostDescription(double? WorkUnits = null, bool AllowsHistoricalEnvelope = true,
    string NumericalRevision = "", string CapacityRevision = "", bool CompleteCombatMatching = false,
    bool QueryLiteralCombatOpening = false, bool ShopPrefixChanged = false);

// Domain preparation has no execution lifetime. Its projections and numerical
// inputs can be shared by planning, Exact and other prediction consumers.
internal sealed record PreparedFilter(string FamilyId)
{
    internal IReadOnlyList<string> Coverage { get; init; } = [FamilyId];
    internal IReadOnlyList<FilterObligation> Obligations { get; init; } = [];
    // Explicit declarations can be narrower than physical Coverage (V and
    // bounded Unknown prefixes). Resolve each child before composing siblings.
    internal IEnumerable<FilterObligation> EffectiveObligations => Obligations.Count > 0 ? Obligations :
        Coverage.Where(domain => domain != VariantOpeningFamily.Id).Select(domain => new FilterObligation(domain));
    internal FamilyAnalyticalCostProjection AnalyticalCost { get; init; } = new(FamilyId, 1, [], "WorkUnavailable");
    internal FamilySurvivalProjection Survival { get; init; } = FamilySurvivalProjection.Unresolved(FamilyId, "ProjectionUnavailable");
    internal FamilyExpectedFilteringCostProjection? ExpectedFilteringCost { get; init; }
    internal bool HasConditionalProjections { get; init; }
    internal Func<IReadOnlySet<string>, FamilySurvivalProjection>? SurvivalResolver { get; init; }
    internal Func<IReadOnlySet<string>, FamilyExpectedFilteringCostProjection?>? ExpectedCostResolver { get; init; }
    internal IReadOnlyList<FilterPreparationIssue> Issues { get; init; } = [];
    internal IReadOnlyList<string> Remainders { get; init; } = [];
    internal FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed) => SurvivalResolver?.Invoke(passed) ?? Survival;
    internal FamilyExpectedFilteringCostProjection? ResolveExpectedFilteringCost(IReadOnlySet<string> passed) =>
        ExpectedCostResolver is null ? ExpectedFilteringCost : ExpectedCostResolver(passed);
}

// This is a value description, not a running invocation. Resource factories may
// capture immutable domain plans, never requests, devices or observation owners.
internal sealed record FilterBodyDescription(PreparedFilter Preparation, string PhysicalRevision, RuntimeProfileId ProfileId)
{
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>(StringComparer.Ordinal);
    internal string FamilyId => Preparation.FamilyId;
    internal IReadOnlyList<string> Coverage => Preparation.Coverage;
    internal FamilyAnalyticalCostProjection AnalyticalCost => Preparation.AnalyticalCost;
    internal FamilySurvivalProjection Survival => Preparation.Survival;
    internal FamilyExpectedFilteringCostProjection? ExpectedFilteringCost => Preparation.ExpectedFilteringCost;
    internal bool HasConditionalProjections => Preparation.HasConditionalProjections;
    internal FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed) => Preparation.ResolveSurvival(passed);
    internal FamilyExpectedFilteringCostProjection? ResolveExpectedFilteringCost(IReadOnlySet<string> passed) => Preparation.ResolveExpectedFilteringCost(passed);
    internal bool UsesGpu { get; init; }
    internal FilterInputModes InputModes { get; init; } = FilterInputModes.All;
    internal int WindowCapacity { get; init; } = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
    internal int CpuChunkCapacity { get; init; } = 65536;
    // Public root-batch preference differs from the body's internal chunk/group capacity.
    internal int? PreferredRootWindowSize { get; init; }
    internal int CpuWorkers { get; init; } = 1;
    internal int PlayersCount { get; init; } = 1;
    internal string InputIdentity { get; init; } = "";
    private GpuNumericRequirements? _numericRequirements;
    // A GPU body must explicitly declare its admitted numerical ABI; absence
    // retains the previous unknown-requirements restriction.
    internal GpuNumericRequirements NumericRequirements
    {
        get => _numericRequirements ?? (UsesGpu ? GpuNumericRequirements.Unspecified : GpuNumericRequirements.None);
        init => _numericRequirements=value;
    }
    internal GpuNumericRequirements GpuNumericRequirements => NumericRequirements;
    internal IReadOnlySet<string>? RequiredPassedCoverage { get; init; }
    internal FilterCostDescription Cost { get; init; } = new();
    internal Func<bool, FamilyConditionPerformanceProjection>? PerformanceProjection { get; init; }
    internal Func<FilterBodyDescription, FilterRuntimeServices, IFilterExecution>? CreateExecution { get; init; }
    internal Func<Godot.RenderingDevice, PrivateOrdinalBuffer?, PrivateOrdinalBuffer?, FamilyPrivateGpuExecution>? BindPrivate { get; init; }
    internal Func<Godot.RenderingDevice, PrivateOrdinalBuffer?, PrivateOrdinalBuffer?, IPrivateGpuScratchLease, FamilyPrivateGpuExecution>? BindPrivateShared { get; init; }
    internal bool SupportsDenseBatchGrouping { get; init; }
    internal bool UsesSmallPrivateOutput { get; init; }
    internal bool RequiresBonesCapsuleDiagnostics { get; init; }
    internal bool CanBindPrivateSerial => BindPrivate is not null;
    internal IReadOnlyList<FilterBodyDescription> CpuAlternatives { get; init; } = [];
    internal IReadOnlyList<FilterBodyDescription> PhysicalBodies { get; init; } = [];
    internal IReadOnlyList<FamilyExecutionPlanStage> EstimationStages { get; init; } = [];
    internal bool IsPrivateTransport { get; init; }
    internal FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    internal FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => PerformanceProjection?.Invoke(compact) ??
        new(FamilyId, PhysicalRevision + (compact ? ".CompactAbi1" : ".Dense"), "PreparedBody", 1, "PreparedNumericalBody", UsesGpu);
    internal FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry, IReadOnlySet<string>? passed = null) =>
        RequiredPassedCoverage is not null && (passed is null || !RequiredPassedCoverage.SetEquals(passed)) ? null :
        Cost.Quote?.Invoke(this, geometry, passed ?? EmptyCoverage);
}

// Historical numerical prices and canonical call prices keep distinct boundary
// identities. An absent descriptor or quote remains unknown, never unsupported.
internal sealed record FilterCostDescription
{
    internal PartyChildCostDescription PartyChild { get; init; } = new();
    internal Func<FilterBodyDescription, FamilyPhysicalQuoteRequest, IReadOnlySet<string>, FamilyPhysicalQuote?>? Quote { get; init; }
    internal Func<FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?>? CalibrationQuote { get; init; }
    internal IReadOnlySet<string>? CalibrationRequiredPassedCoverage { get; init; }
    internal Func<bool, int>? MinimumComparableInput { get; init; }
    internal string CompleteCallPeakKey { get; init; } = "";
    internal Func<FamilyPhysicalQuoteRequest, IReadOnlySet<string>, FamilyUnitCost?>? CompleteCallQuote { get; init; }
    internal bool CanonicalScope { get; init; }
    internal Func<bool, string?>? CanonicalWorkShape { get; init; }
    internal Func<bool, int, string?>? OrderedChildBodies { get; init; }
    internal Func<bool, string>? OptimizationShape { get; init; }
    internal string PrivateOutputRevision { get; init; } = "";
    internal Func<FamilyPhysicalQuoteRequest, string>? ObservationRevision { get; init; }
    internal string PublicBoundaryToken { get; init; } = "";
    internal string PublicBoundaryMeasuredMarker { get; init; } = "";
    internal bool ComposedPopulationReference { get; init; }
    internal bool PrivateCanonicalScope { get; init; } = true;
    internal Func<bool, string>? PrivatePhysicalRevision { get; init; }
    internal string PrivateTransportPath { get; init; } = "CurrentFamilyBinding";
    internal string PrivateBodyRevision { get; init; } = "";
    internal string PrivateOutputPathSuffix { get; init; } = "";
}
