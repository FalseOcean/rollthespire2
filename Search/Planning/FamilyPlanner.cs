using System.Globalization;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum FamilyPlannerRankingAuthority
{
    FixedExecutionOrder,
    UnpricedPhysicalTrial,
    GreedyUnitEliminationCost
}

internal sealed record FamilyExecutionPlanStage(
    IFamilyInvocation Family,
    IReadOnlyList<string> Coverage,
    bool CompactAbi1Input,
    double ExpectedInputFraction,
    double? Survival,
    double? RejectionProbability,
    double? ExpectedFilteringWorkUnitsPerInput,
    FamilyConditionPerformanceProjection ConditionPerformance,
    FamilyPerformanceReference Performance,
    double? PerInputTimeMs,
    double? ExpectedStageTimeMsPerRoot,
    double? TimePerRejectedCandidateMs,
    double? WorkPerRejectedCandidate,
    int RegistrationIndex);

// Evidence of the bounded N/A preparation fallback actually selected, not an
// admission capability or an instruction to recover a running GPU invocation.
internal sealed record FamilyGpuPreparationFallback(string FamilyId, string Scope, string Detail);

internal sealed record FamilyExecutionPlan(
    string PlanId,
    string SelectionPolicyId,
    FamilyPlannerRankingAuthority RankingAuthority,
    IReadOnlyList<FamilyExecutionPlanStage> Stages,
    double? ExpectedFamilyPipelineMsPerRoot,
    IReadOnlyList<string> DecisionEvidence,
    IReadOnlyList<string> MissingEvidence)
{
    internal bool AllowSmallCandidateExact { get; init; }
    internal bool AllConditionsUnfiltered { get; init; }
    internal bool FinalValidationSkipped { get; init; }
    internal IReadOnlyList<FamilyGpuPreparationFallback> GpuPreparationFallbacks { get; init; } = [];
    internal IReadOnlyList<string> FilterRemainders { get; init; } = [];
    // Allocation-specific TerminalFastCandidateRate, not QueryHitProbability.
    // StageSurvival lives in the selected stage projections; stronger Composites
    // may have a different terminal rate for the same logical Coverage.
    internal double? EstimatedTerminalSurvival { get; init; }
    internal IReadOnlyList<IFamilyInvocation> OrderedFamilies => Stages.Select(stage => stage.Family).ToArray();
    // Private ordinal transport is one runtime invocation, but preserves the
    // selected public units and their exact order internally.
    internal IReadOnlyList<IFamilyInvocation> ExecutionOrderUnits => OrderedFamilies.SelectMany(family =>
        family is AutomaticPrivateSerialInvocation serial ? serial.PhysicalFamilies : [family]).ToArray();

    internal string FormatSummary()
    {
        string stages = Stages.Count == 0
            ? FinalValidationSkipped ? "UnfilteredSeedEnumeration" : "DirectProductionExact"
            : string.Join('|', Stages.Select((stage, index) =>
                $"{index + 1}:{stage.Family.FamilyId}" +
                $"[coverage={string.Join(',', stage.Coverage)}" +
                $",inputMode={(stage.CompactAbi1Input ? "CompactAbi1" : "Dense")}" +
                $",expectedInputFraction={Maybe(stage.ExpectedInputFraction)}" +
                $",survival={Maybe(stage.Survival)}" +
                $",rejection={Maybe(stage.RejectionProbability)}" +
                $",physicalRevision={San(stage.Performance.PhysicalImplementationRevision)}" +
                $",performance={Maybe(Positive(stage.Performance.CurrentQueryReference))}" +
                $",performanceSource={stage.Performance.EvidenceSource}" +
                $",perInputMs={Maybe(stage.PerInputTimeMs)}" +
                $",expectedStageMsPerRoot={Maybe(stage.ExpectedStageTimeMsPerRoot)}" +
                $",timePerRejectedCandidateMs={Maybe(stage.TimePerRejectedCandidateMs)}" +
                $",expectedFilteringWorkPerInput={Maybe(stage.ExpectedFilteringWorkUnitsPerInput)}]"));
        return $"planId={PlanId};selectionPolicy={SelectionPolicyId};rankingAuthority={RankingAuthority};" +
               $"expectedFamilyPipelineMsPerRoot={Maybe(ExpectedFamilyPipelineMsPerRoot)};stages={stages};" +
               $"estimatedTerminalSurvival={Maybe(EstimatedTerminalSurvival)};" +
               $"filterRemainders={(FilterRemainders.Count == 0 ? "none" : string.Join('|', FilterRemainders.Select(San)))};" +
               $"decisionEvidence={(DecisionEvidence.Count == 0 ? "none" : string.Join('|', DecisionEvidence.Select(San)))};" +
               $"missingEvidence={(MissingEvidence.Count == 0 ? "none" : string.Join('|', MissingEvidence.Select(San)))};" +
               $"allowSmallCandidateExact={AllowSmallCandidateExact};permutationEnumeration=false;searchAdmissionAffected=false";
    }

    private static double? Positive(double value) => value > 0d && double.IsFinite(value) ? value : null;
    private static string Maybe(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("G17", CultureInfo.InvariantCulture)
        : "unavailable";
    private static string San(string value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>Orders necessary filters by the local cost per expected rejection.</summary>
internal static class FamilyPlanner
{
    internal const string SelectionPolicyId = "FamilyPlanner.GreedyUnitEliminationCost.20261026.v2";

    // Fixed construction is reserved for explicit physical trials and transport
    // wrappers. It does not compare complete plans or enable early Exact.
    internal static FamilyExecutionPlan InOrder(IReadOnlyList<IFamilyInvocation> families)
    {
        var costs = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(nameof(FamilyPlanner), "CostSnapshot", GpuCostCalibration.Capture, null);
        return BuildPlan(BuildCandidates(families, costs), FamilyPlannerRankingAuthority.FixedExecutionOrder,
            ["SpecifiedExecutionOrder"]);
    }

    internal static FamilyExecutionPlan InManualOrder(IReadOnlyList<IFamilyInvocation> available,
        IReadOnlyList<IFamilyInvocation> required, IReadOnlyList<string> requested,
        IFamilyInvocation? denseOnlyEntry = null)
    {
        if (requested.Any(string.IsNullOrWhiteSpace) || requested.Distinct(StringComparer.Ordinal).Count() != requested.Count)
            throw new InvalidOperationException("ManualFamilyOrder.InvalidOrDuplicateUnit");
        var authoredCoverage = requested.SelectMany(key => key.Split('+')).ToArray();
        if (authoredCoverage.Distinct(StringComparer.Ordinal).Count() != authoredCoverage.Length)
            throw new InvalidOperationException("ManualFamilyOrder.OverlappingUnits");
        var expected = required.SelectMany(family => family.Coverage).ToHashSet(StringComparer.Ordinal);
        var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
        var current = new List<IFamilyInvocation>();
        // Retain still available authored groups without invoking greedy cost
        // selection. Changed predicates can remove a group or split its body;
        // ordinary current units fill the remaining obligations below.
        foreach (string key in requested)
        {
            var unit = available.FirstOrDefault(family => FamilyExecutionCoordinator.FamilyOrderKey(family) == key &&
                family.Coverage.All(remaining.Contains));
            if (unit is null) continue;
            current.Add(unit);
            remaining.ExceptWith(unit.Coverage);
        }
        foreach (var unit in required)
        {
            if (!unit.Coverage.All(remaining.Contains)) continue;
            current.Add(unit);
            remaining.ExceptWith(unit.Coverage);
        }
        var reconciled = FamilyExecutionCoordinator.ReconcileFamilyOrder(requested, current);
        var ordered = reconciled.Select(key => current.Single(family => FamilyExecutionCoordinator.FamilyOrderKey(family) == key)).ToArray();
        var actual = ordered.SelectMany(family => family.Coverage).ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || !expected.SetEquals(actual))
            throw new InvalidOperationException("ManualFamilyOrder.CoverageChanged:expected=" + string.Join(',', expected.Order(StringComparer.Ordinal)) +
                ";requested=" + string.Join(',', actual));
        if (denseOnlyEntry is not null && Array.IndexOf(ordered, denseOnlyEntry) > 0)
            throw new InvalidOperationException("ManualFamilyOrder.DenseOnlyUnitMustBeFirst:" +
                FamilyExecutionCoordinator.FamilyOrderKey(denseOnlyEntry));
        var costs = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(nameof(FamilyPlanner), "CostSnapshot", GpuCostCalibration.Capture, null);
        var selected = BuildPlan(BuildCandidates(ordered, costs), FamilyPlannerRankingAuthority.FixedExecutionOrder,
            ["ManualFamilyOrder;NoAutomaticReordering;WholeCoverageUnits;MissingCostDoesNotBlockExecution",
                "EffectiveManualFamilyOrder=" + string.Join('>', reconciled)], resolveInputReferences: true, costs: costs);
        return selected with { SelectionPolicyId = "FamilyPlanner.ManualOrder.20261027.v1", AllowSmallCandidateExact = true,
            EstimatedTerminalSurvival = selected.Stages.All(stage => stage.Survival.HasValue)
                ? selected.Stages.Aggregate(1d, (fraction, stage) => fraction * stage.Survival!.Value) : null };
    }

    internal static FamilyExecutionPlan Plan(IReadOnlyList<IFamilyInvocation> families) => PlanSimple(families);

    private sealed record Candidate(
        IFamilyInvocation Family,
        string[] Coverage,
        double? Survival,
        double? Rejection,
        double? DenseWorkPerInput,
        double? CompactWorkPerInput,
        FamilyConditionPerformanceProjection DenseCondition,
        FamilyConditionPerformanceProjection CompactCondition,
        FamilyPerformanceReference DensePerformance,
        FamilyPerformanceReference CompactPerformance,
        double? DensePerInputMs,
        double? CompactPerInputMs,
        int RegistrationIndex);

    // Every ordinary Search uses this walk. A known Dense entry is useful without a
    // price for every sparse suffix. Existing N+T/N+R fused bodies may compete
    // as whole units; selecting either constituent removes that overlapping
    // alternative. This is one greedy walk, not complete-allocation enumeration.
    internal static FamilyExecutionPlan PlanSimple(IReadOnlyList<IFamilyInvocation> registeredFamilies,
        IReadOnlyList<IFamilyInvocation>? fusedUnits = null, bool includeCpuAlternatives = false,
        IFamilyInvocation? denseEntry = null)
    {
        ArgumentNullException.ThrowIfNull(registeredFamilies);
        var costSnapshot = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(nameof(FamilyPlanner), "CostSnapshot", GpuCostCalibration.Capture, null);
        Candidate[] registered = BuildCandidates(registeredFamilies, costSnapshot);
        var required = registered.SelectMany(candidate => candidate.Coverage).ToHashSet(StringComparer.Ordinal);
        fusedUnits ??= [];
        foreach (var unit in fusedUnits)
            if (unit is null || unit.Coverage.Count < 2 || !unit.Coverage.All(required.Contains))
                throw new InvalidOperationException("SimplePlanning.FusedCoverageMustReplaceRegisteredObligations");
        var cpuAlternatives = includeCpuAlternatives ? registeredFamilies.SelectMany(family =>
            FamilyAuxiliaryEvidence.Read<IFamilyInvocation[]>(family.FamilyId, "SimplePlanner.CpuRealizations",
                () =>
                {
                    var alternatives = FamilyCpuExecution.ConfiguredRealizations(family).ToArray();
                    if (alternatives.Any(alternative => alternative.Coverage.Count != 1 ||
                        !alternative.Coverage.ToHashSet(StringComparer.Ordinal).SetEquals(family.Coverage)))
                        throw new InvalidOperationException("SimplePlanning.CpuAlternativeCoverageMismatch");
                    return alternatives;
                }, [])).ToArray() : [];
        Candidate[] candidates = fusedUnits.Count == 0 && cpuAlternatives.Length == 0 ? registered :
            BuildCandidates([..registeredFamilies, ..fusedUnits, ..cpuAlternatives], costSnapshot, allowEquivalentCoverage: true);
        var remaining = new HashSet<string>(required, StringComparer.Ordinal);
        var passed = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<Candidate>();
        if (denseEntry is not null)
        {
            // These existing coupled numerical bodies accept Dense input only.
            // Their physical constraint fixes one entry unit, while every
            // independent remainder still uses the same local greedy rule.
            Candidate entry = BuildCandidates([denseEntry], costSnapshot)[0] with { RegistrationIndex = -1 };
            if (!entry.Coverage.All(required.Contains))
                throw new InvalidOperationException("SimplePlanning.DenseEntryCoverageMismatch");
            ordered.Add(entry);
            passed.UnionWith(entry.Coverage);
            remaining.ExceptWith(entry.Coverage);
        }
        var decisions = new List<string> { "GreedyUnitCostPerRejectedCandidate;DenseEntryThenCompact;NoCompleteSuffixQuoteRequired",
            "NoPermutationOrCpuGpuCombinationEnumeration;NoTargetKRootHorizonOrExactTailPriceRequired",
            "CpuAlternativesRequirePricedRegisteredBodyInSameInputRole;UnknownNotInfinity" };
        while (remaining.Count > 0)
        {
            bool compact = ordered.Count > 0;
            double? HardwareCost(Candidate c) => compact ? c.CompactPerInputMs : c.DensePerInputMs;
            double? HardwareScore(Candidate c) => GreedyUnitOrder.Score(HardwareCost(c), c.Survival);
            Candidate[] resolved = candidates.Where(candidate => candidate.Coverage.All(remaining.Contains))
                .Select(candidate =>
                {
                    double? survival = ReadSurvival(candidate.Family, passed).SurvivalProbability;
                    var expected = ReadExpectedCost(candidate.Family, passed);
                    candidate = candidate with { Survival = survival,
                        Rejection = survival.HasValue ? 1d - survival.Value : null,
                        DenseWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput,
                        CompactWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput + expected?.CompactInputWorkUnitsPerInput };
                    if (candidate.Family is PartyFamily or FamilyCpuExecution)
                    {
                        var performance = ReadReference(costSnapshot, candidate.Family,
                            candidate.Family.ResolveConditionPerformance(compact), compact, passed);
                        double? perInputMs = FamilyPerformanceProjectionMath.PerInputMilliseconds(performance);
                        candidate = compact
                            ? candidate with { CompactPerformance = performance, CompactPerInputMs = perInputMs }
                            : candidate with { DensePerformance = performance, DensePerInputMs = perInputMs };
                    }
                    return candidate;
                }).ToArray();
            // An optional CPU body needs a comparable price for the registered
            // body in this same input role. A known CPU price cannot turn an
            // executable unpriced integer GPU body into an infinitely costly one.
            Candidate[] available = resolved.Where(candidate => candidate.RegistrationIndex < registered.Length + fusedUnits.Count ||
                HardwareScore(candidate).HasValue && resolved.Any(original => original.RegistrationIndex < registered.Length &&
                    original.Coverage.SequenceEqual(candidate.Coverage) && HardwareScore(original).HasValue))
                // Unpriced optional fusions do not create a new barrier among
                // the original obligations; manual selection still retains them.
                .OrderBy(candidate => candidate.RegistrationIndex >= registered.Length && !HardwareScore(candidate).HasValue
                    ? int.MaxValue : registered.Where(original => original.Coverage.Any(candidate.Coverage.Contains))
                        .Select(original => original.RegistrationIndex).DefaultIfEmpty(candidate.RegistrationIndex).Min())
                .ThenBy(candidate => candidate.RegistrationIndex).ToArray();
            if (available.Length == 0) throw new InvalidOperationException("SimplePlanning.RegisteredCoverageLost");
            double? AnalyticalCost(Candidate c) => compact ? c.CompactWorkPerInput : c.DenseWorkPerInput;
            var selected = GreedyUnitOrder.Select(available, HardwareCost, c => c.Survival, AnalyticalCost);
            string basis = available.Any(candidate => HardwareScore(candidate).HasValue)
                ? HardwareScore(selected).HasValue ? "HardwareTimePerRejectedCandidate" : "RegistrationOrderUnknownCostOrSurvival"
                : GreedyUnitOrder.Score(AnalyticalCost(selected), selected.Survival).HasValue
                    ? "AnalyticalWorkPerRejectedCandidate" : "RegistrationOrderEvidenceUnavailable";
            double? selectedScore = basis == "HardwareTimePerRejectedCandidate"
                ? HardwareScore(selected) : GreedyUnitOrder.Score(AnalyticalCost(selected), selected.Survival);
            decisions.Add($"GreedyStage={ordered.Count + 1};family={selected.Family.FamilyId};coverage={string.Join(',', selected.Coverage)};" +
                $"inputMode={(compact ? "CompactAbi1" : "Dense")};passedCoverage={string.Join(',', passed.Order(StringComparer.Ordinal))};" +
                $"basis={basis};score={SimpleScoreText(selectedScore)};survival={Maybe(selected.Survival)};" +
                $"unpricedAlternativesRetained={available.Count(candidate => !HardwareScore(candidate).HasValue)}");
            ordered.Add(selected);
            passed.UnionWith(selected.Coverage);
            remaining.ExceptWith(selected.Coverage);
        }
        if (denseEntry is not null) decisions.Insert(0,
            "CoupledDenseOnlyEntry=" + denseEntry.ResolveConditionPerformance(false).PhysicalImplementationRevision +
            ";IndependentRemainderUsesGreedyRule;NoCompletePlanComparison");
        var result = BuildPlan(ordered.ToArray(), FamilyPlannerRankingAuthority.GreedyUnitEliminationCost, decisions);
        return result with { AllowSmallCandidateExact = true,
            EstimatedTerminalSurvival = result.Stages.All(stage => stage.Survival.HasValue)
                ? result.Stages.Aggregate(1d, (fraction, stage) => fraction * stage.Survival!.Value) : null };
    }

    private static string SimpleScoreText(double? score) => score == double.PositiveInfinity ? "NoPredictedRejection" : Maybe(score);

    private static Candidate[] BuildCandidates(IReadOnlyList<IFamilyInvocation> registeredFamilies,
        GpuCostSnapshot? costSnapshot, bool allowEquivalentCoverage = false)
    {
        var candidates = new Candidate[registeredFamilies.Count];
        var covered = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < registeredFamilies.Count; index++)
        {
            IFamilyInvocation family = registeredFamilies[index] ??
                throw new ArgumentException("Registered Family cannot be null.", nameof(registeredFamilies));
            string[] coverage = family.Coverage
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            if (coverage.Length == 0) throw new InvalidOperationException($"FamilyCoverageEmpty:{family.FamilyId}");
            foreach (string domain in coverage)
            {
                if (!covered.Add(domain) && !allowEquivalentCoverage)
                    throw new InvalidOperationException($"FamilyCoverageOverlapRequiresExplicitAlternativeSelection:{domain}");
            }

            double? survival = ReadSurvival(family).SurvivalProbability;
            double? rejection = survival.HasValue ? 1d - survival.Value : null;
            FamilyExpectedFilteringCostProjection? expectedCost = ReadExpectedCost(family);
            double? denseWork = expectedCost?.ExpectedFilteringWorkUnitsPerInput;
            double? compactWork = denseWork.HasValue
                ? denseWork.Value + expectedCost!.CompactInputWorkUnitsPerInput
                : null;
            FamilyConditionPerformanceProjection denseCondition = family.ResolveConditionPerformance(false);
            FamilyConditionPerformanceProjection compactCondition = family.ResolveConditionPerformance(true);
            FamilyPerformanceReference densePerformance = ReadReference(costSnapshot, family, denseCondition, false);
            FamilyPerformanceReference compactPerformance = ReadReference(costSnapshot, family, compactCondition, true);
            candidates[index] = new Candidate(
                family, coverage, survival, rejection, denseWork, compactWork,
                denseCondition, compactCondition, densePerformance, compactPerformance,
                FamilyPerformanceProjectionMath.PerInputMilliseconds(densePerformance),
                FamilyPerformanceProjectionMath.PerInputMilliseconds(compactPerformance), index);
        }
        return candidates;
    }

    private static FamilyExecutionPlan BuildPlan(
        Candidate[] ordered,
        FamilyPlannerRankingAuthority authority,
        IReadOnlyList<string> decision, bool resolveInputReferences = false, GpuCostSnapshot? costs = null)
    {
        var missing = new List<string>();
        var stages = new FamilyExecutionPlanStage[ordered.Length];
        double inputFraction = 1d;
        double expectedPipelineMsPerRoot = 0d;
        bool completeTime = true;
        var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < ordered.Length; index++)
        {
            Candidate candidate = ordered[index];
            FamilySurvivalProjection survival = ReadSurvival(candidate.Family, passedCoverage);
            FamilyExpectedFilteringCostProjection? expected = ReadExpectedCost(candidate.Family, passedCoverage);
            candidate = candidate with
            {
                Survival = survival.SurvivalProbability,
                Rejection = survival.SurvivalProbability.HasValue ? 1d - survival.SurvivalProbability.Value : null,
                DenseWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput,
                CompactWorkPerInput = expected?.ExpectedFilteringWorkUnitsPerInput + expected?.CompactInputWorkUnitsPerInput
            };
            bool compact = index > 0;
            FamilyConditionPerformanceProjection condition = compact ? candidate.CompactCondition : candidate.DenseCondition;
            FamilyPerformanceReference performance = compact ? candidate.CompactPerformance : candidate.DensePerformance;
            double? perInputMs = compact ? candidate.CompactPerInputMs : candidate.DensePerInputMs;
            if (resolveInputReferences)
            {
                // Conditional CPU and party prices belong to the actual
                // prefix/input role; an absent quote remains unknown.
                if (candidate.Family is FamilyCpuExecution or PartyFamily)
                    performance = ReadReference(costs, candidate.Family, condition, compact, passedCoverage);
                perInputMs = FamilyPerformanceProjectionMath.PerInputMilliseconds(performance);
            }
            double? expectedWork = compact ? candidate.CompactWorkPerInput : candidate.DenseWorkPerInput;
            double? expectedStageMs = perInputMs.HasValue && double.IsFinite(inputFraction)
                ? inputFraction * perInputMs.Value
                : null;
            if (expectedStageMs.HasValue) expectedPipelineMsPerRoot += expectedStageMs.Value;
            else completeTime = false;

            if (!candidate.Survival.HasValue) missing.Add($"FamilySurvivalUnavailable:{candidate.Family.FamilyId}");
            if (!perInputMs.HasValue)
                missing.Add($"FamilyHardwarePerformanceUnavailable:{candidate.Family.FamilyId}:{(compact ? "CompactAbi1" : "Dense")}");
            if (!expectedWork.HasValue) missing.Add($"FamilyExpectedFilteringCostUnavailable:{candidate.Family.FamilyId}");
            stages[index] = new FamilyExecutionPlanStage(
                candidate.Family, candidate.Coverage, compact, inputFraction,
                candidate.Survival, candidate.Rejection, expectedWork, condition, performance,
                perInputMs, expectedStageMs, GreedyUnitOrder.Score(perInputMs, candidate.Survival),
                GreedyUnitOrder.Score(expectedWork, candidate.Survival), candidate.RegistrationIndex);
            if (candidate.Survival.HasValue) inputFraction *= candidate.Survival.Value;
            else inputFraction = double.NaN;
            passedCoverage.UnionWith(candidate.Coverage);
        }

        string planId = "FamilyPlan.v2." + string.Join('>', stages.Select(stage =>
            stage.Family.FamilyId + "@" + stage.Performance.PhysicalImplementationRevision));
        return new FamilyExecutionPlan(
            planId, SelectionPolicyId, authority, stages,
            completeTime ? expectedPipelineMsPerRoot : null,
            decision,
            missing.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static FamilySurvivalProjection ReadSurvival(IFamilyInvocation family, IReadOnlySet<string>? passed = null) =>
        FamilyAuxiliaryEvidence.Read(family.FamilyId, "Planner.Survival",
            () => passed is null ? family.Survival : family.ResolveSurvival(passed),
            FamilySurvivalProjection.Unresolved(family.FamilyId, "AuxiliarySurvivalFailed"));

    private static FamilyExpectedFilteringCostProjection? ReadExpectedCost(IFamilyInvocation family, IReadOnlySet<string>? passed = null) =>
        FamilyAuxiliaryEvidence.Read<FamilyExpectedFilteringCostProjection?>(family.FamilyId, "Planner.ExpectedCost",
            () => passed is null ? family.ExpectedFilteringCost : family.ResolveExpectedFilteringCost(passed), null);

    private static FamilyPerformanceReference ReadReference(GpuCostSnapshot? costs, IFamilyInvocation family,
        FamilyConditionPerformanceProjection condition, bool compact, IReadOnlySet<string>? passed = null) =>
        FamilyAuxiliaryEvidence.Read(family.FamilyId, "Planner.PhysicalQuote",
            () => GpuCostSnapshot.ComputationReference(costs, family, condition, compact, passed,
                referenceRootWindow: passed is not null && family is FamilyCpuExecution cpu
                    ? compact ? PrivateOrdinalBuffer.Capacity : cpu.WindowCapacity : null,
                referencePopulation: passed is null ? null : family is FamilyCpuExecution cpuFamily
                    ? cpuFamily.WindowCapacity : FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize),
            UnavailableReference(family, condition));

    private static FamilyPerformanceReference UnavailableReference(IFamilyInvocation family, FamilyConditionPerformanceProjection condition) =>
            new FamilyPerformanceReference(family.FamilyId, condition.PhysicalImplementationRevision,
                condition.Factor, 0d, FamilyPerformanceEvidenceSource.Unavailable, "", "", "AuxiliaryQuoteFailed");

    private static string Maybe(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? F(value.Value)
        : "unavailable";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
