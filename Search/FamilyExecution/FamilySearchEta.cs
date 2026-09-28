using System.Globalization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum FamilySearchEtaStatus
{
    Ready,
    Calibrating,
    Unavailable,
    Impossible
}

internal sealed record FamilyEtaStageProjection(
    string FamilyId,
    bool CompactAbi1Input,
    double InputPopulation,
    double OutputPopulation,
    double Survival,
    string PhysicalImplementationRevision,
    double? ReferenceCandidatesPerSecond,
    FamilyPerformanceEvidenceSource EvidenceSource,
    double? SteadyStateMs);

internal sealed record FamilySearchEtaProjectionV1(
    FamilySearchEtaStatus Status,
    double? AcceptedResultProbability,
    string AcceptedResultProbabilityEvidence,
    FamilyExecutionPlan Plan,
    int TargetCount,
    long ScanCount,
    long? FirstMeanRootHorizon,
    long? FirstP99RootHorizon,
    long? TargetMeanRootHorizon,
    double? FirstResultMeanMs,
    double? FirstResultP99Ms,
    double? TargetMeanMs,
    double? FixedScanRangeMs,
    double? ExpectedMatchesWithinScanRange,
    double? TargetProbabilityWithinScanRange,
    double? TargetFamilyPipelineMs,
    double? TargetExactTailMs,
    IReadOnlyList<FamilyEtaStageProjection> TargetStages,
    IReadOnlyList<string> MissingEvidence)
{
    internal double? FirstResultSearchMeanMs { get; init; }
    internal double? FirstResultSearchP99Ms { get; init; }
    internal double? TargetSearchMeanMs { get; init; }

    internal string FormatSummary()
    {
        string stages = TargetStages.Count == 0
            ? "none"
            : string.Join('|', TargetStages.Select(stage =>
                $"{stage.FamilyId}[inputMode={(stage.CompactAbi1Input ? "CompactAbi1" : "Dense")}," +
                $"input={F(stage.InputPopulation)},output={F(stage.OutputPopulation)}," +
                $"survival={F(stage.Survival)},physicalRevision={San(stage.PhysicalImplementationRevision)}," +
                $"referenceCps={Maybe(stage.ReferenceCandidatesPerSecond)},source={stage.EvidenceSource}," +
                $"steadyMs={Maybe(stage.SteadyStateMs)}]"));
        return $"familySearchEta=true;status={Status};acceptedResultProbability={Maybe(AcceptedResultProbability)};" +
               $"acceptedResultProbabilityEvidence={San(AcceptedResultProbabilityEvidence)};" +
               $"selectedPlan={San(Plan.PlanId)};selectionPolicy={San(Plan.SelectionPolicyId)};" +
               $"targetCount={TargetCount};scanCount={ScanCount};" +
               $"firstMeanRoots={Maybe(FirstMeanRootHorizon)};firstP99Roots={Maybe(FirstP99RootHorizon)};" +
               $"targetMeanRoots={Maybe(TargetMeanRootHorizon)};" +
               $"firstResultMeanMs={Maybe(FirstResultMeanMs)};firstResultP99Ms={Maybe(FirstResultP99Ms)};" +
               $"targetMeanMs={Maybe(TargetMeanMs)};fixedScanRangeMs={Maybe(FixedScanRangeMs)};" +
               $"playerFirstSearchMeanMs={Maybe(FirstResultSearchMeanMs)};playerFirstSearchP99Ms={Maybe(FirstResultSearchP99Ms)};playerTargetSearchMeanMs={Maybe(TargetSearchMeanMs)};playerGpuColdSetupIncluded=false;" +
               $"expectedMatchesWithinScanRange={Maybe(ExpectedMatchesWithinScanRange)};" +
               $"targetProbabilityWithinScanRange={Maybe(TargetProbabilityWithinScanRange)};" +
               $"targetFamilyPipelineMs={Maybe(TargetFamilyPipelineMs)};targetExactTailMs={Maybe(TargetExactTailMs)};" +
               $"targetStages={stages};missingEvidence={(MissingEvidence.Count == 0 ? "none" : string.Join('|', MissingEvidence.Select(San)))};" +
               "steadyStateComposition=max(FamilyPipeline,ExactTail);sameBatchFamiliesOrdered=true;" +
               $"crossBatchFamilyExactOverlap=true;startupIncluded={Plan.EstimatedSetupMilliseconds.HasValue};" +
               $"completeQuote={San(Plan.CompleteQuoteEvidence)};runtimeSurvivalLearning=false;searchAdmissionAffected=false";
    }

    private static string Maybe(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";
    private static string Maybe(double? value) => value.HasValue && double.IsFinite(value.Value) ? F(value.Value) : "unavailable";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string San(string value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>
/// Steady-state Family-world ETA. Query-wide probability owns result rarity;
/// Family Survival owns stage populations; physical references own Family time;
/// and the existing timing-only Exact store owns the CPU tail.
/// </summary>
internal static class FamilySearchEtaProjectorV1
{
    private const double FirstResultP99Confidence = 0.99d;

    private sealed record HorizonProjection(
        double? TotalMs,
        double? FamilyPipelineMs,
        double? ExactTailMs,
        IReadOnlyList<FamilyEtaStageProjection> Stages,
        IReadOnlyList<string> MissingEvidence);

    internal static FamilySearchEtaProjectionV1 Project(
        ExactSearchExecutionRequest request,
        FamilyExecutionPlan plan,
        IReadOnlyDictionary<string, double>? livePerformanceByPhysicalRevision = null,
        double? liveExactMsPerAttempt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        int targetCount = Math.Max(1, request.TargetMatchCount);
        JointSelectivityResult joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        SearchVerificationFamily exactFamily = FamilyExactTimingDomains.Resolve(request);
        SearchExactTimingEvidence? exactTiming = SearchPredictabilityVerificationStore.TryGetExactTiming(exactFamily);

        HorizonProjection scan = ProjectHorizon(
            request.ScanCount, request, plan, exactTiming, exactFamily,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);
        var missing = new List<string>(scan.MissingEvidence);
        if (joint.ExactlyImpossible && joint.Probability == 0d)
        {
            return Build(
                FamilySearchEtaStatus.Impossible, 0d, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.TotalMs, 0d, 0d,
                null, null, [], missing);
        }

        if (!joint.JointlyPriced || joint.Probability is not double probability)
        {
            missing.Add("AcceptedResultProbabilityUnavailable:" + joint.EvidenceCode);
            missing.AddRange(joint.UnknownComponents.Select(item => "QueryProbabilityComponentUnavailable:" + item));
            return Build(
                FamilySearchEtaStatus.Unavailable, null, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.TotalMs, null, null,
                null, null, [], missing);
        }
        if (!(probability > 0d && probability <= 1d) || !double.IsFinite(probability))
        {
            missing.Add("AcceptedResultProbabilityInvalid");
            return Build(
                FamilySearchEtaStatus.Unavailable, null, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.TotalMs, null, null,
                null, null, [], missing);
        }

        if (!TryCeil(SearchPredictabilityMath.ExpectedRootsToFirst(probability), out long firstMeanRoots) ||
            !TryRootsForAtLeastOneConfidence(probability, FirstResultP99Confidence, out long firstP99Roots) ||
            !TryCeil(Math.Min(request.ScanCount, SearchPredictabilityMath.ExpectedRootsToTarget(probability, targetCount)), out long targetMeanRoots))
        {
            missing.Add("QueryProbabilityRootHorizonOverflow");
            return Build(
                FamilySearchEtaStatus.Unavailable, probability, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.TotalMs,
                probability * request.ScanCount,
                SearchPredictabilityMath.ProbabilityAtLeastTarget(probability, request.ScanCount, targetCount),
                null, null, [], missing);
        }

        HorizonProjection firstMean = ProjectHorizon(
            firstMeanRoots, request, plan, exactTiming, exactFamily,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);
        HorizonProjection firstP99 = ProjectHorizon(
            firstP99Roots, request, plan, exactTiming, exactFamily,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);
        HorizonProjection target = ProjectHorizon(
            Math.Min(targetMeanRoots, request.ScanCount), request, plan, exactTiming, exactFamily,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);
        missing.AddRange(firstMean.MissingEvidence);
        missing.AddRange(firstP99.MissingEvidence);
        missing.AddRange(target.MissingEvidence);
        string[] distinctMissing = Distinct(missing);
        FamilySearchEtaStatus status = SearchTime(target).HasValue && SearchTime(firstMean).HasValue && SearchTime(firstP99).HasValue
            ? FamilySearchEtaStatus.Ready
            : distinctMissing.Any(e => e.StartsWith("FamilyHardwarePerformanceUnavailable:",StringComparison.Ordinal) ||
                e.StartsWith("CompleteAllocationQuoteUnavailable:",StringComparison.Ordinal) ||
                e.StartsWith("FamilySurvivalUnavailable:",StringComparison.Ordinal) ||
                e.StartsWith("CompleteAllocationTerminalSurvivalUnavailable",StringComparison.Ordinal))
                ? FamilySearchEtaStatus.Unavailable : FamilySearchEtaStatus.Calibrating;
        return Build(
            status, probability, joint.EvidenceCode, plan, targetCount, request.ScanCount,
            firstMeanRoots, firstP99Roots, targetMeanRoots,
            firstMean.TotalMs, firstP99.TotalMs, target.TotalMs, scan.TotalMs,
            probability * request.ScanCount,
            SearchPredictabilityMath.ProbabilityAtLeastTarget(probability, request.ScanCount, targetCount),
            target.FamilyPipelineMs, target.ExactTailMs, target.Stages, distinctMissing) with
        {
            FirstResultSearchMeanMs = SearchTime(firstMean),
            FirstResultSearchP99Ms = SearchTime(firstP99),
            TargetSearchMeanMs = SearchTime(target)
        };
    }

    private static double? SearchTime(HorizonProjection horizon) =>
        horizon.FamilyPipelineMs is double family && horizon.ExactTailMs is double exact
            ? Math.Max(family, exact) : null;

    private static HorizonProjection ProjectHorizon(
        double roots,
        ExactSearchExecutionRequest request,
        FamilyExecutionPlan plan,
        SearchExactTimingEvidence? exactTiming,
        SearchVerificationFamily exactFamily,
        IReadOnlyDictionary<string, double>? livePerformanceByPhysicalRevision,
        double? liveExactMsPerAttempt)
    {
        var missing = new List<string>();
        double population = roots;
        double familyPipelineMs = 0d;
        bool familyComplete = true;
        var stages = new List<FamilyEtaStageProjection>(plan.Stages.Count);
        foreach (FamilyExecutionPlanStage stage in plan.Stages)
        {
            if (stage.Survival is not double survival)
            {
                missing.Add("FamilySurvivalUnavailable:" + stage.Family.FamilyId);
                familyComplete = false;
                break;
            }
            double output = population * survival;
            double liveReference = 0d;
            bool hasLiveReference = livePerformanceByPhysicalRevision is not null &&
                                    livePerformanceByPhysicalRevision.TryGetValue(
                                        stage.Performance.PhysicalImplementationRevision, out liveReference) &&
                                    liveReference > 0d && double.IsFinite(liveReference);
            double? reference = hasLiveReference
                ? liveReference
                : stage.Performance.CurrentQueryReference > 0d && double.IsFinite(stage.Performance.CurrentQueryReference)
                    ? stage.Performance.CurrentQueryReference
                    : null;
            double? time = reference.HasValue
                ? FamilyPerformanceProjectionMath.ExpectedMilliseconds(population, reference.Value)
                : null;
            if (!time.HasValue)
            {
                missing.Add("FamilyHardwarePerformanceUnavailable:" + stage.Family.FamilyId);
                familyComplete = false;
            }
            else
            {
                familyPipelineMs += time.Value;
            }
            stages.Add(new FamilyEtaStageProjection(
                stage.Family.FamilyId, stage.CompactAbi1Input, population, output, survival,
                stage.Performance.PhysicalImplementationRevision,
                reference,
                hasLiveReference ? FamilyPerformanceEvidenceSource.Live : stage.Performance.EvidenceSource,
                time));
            population = output;
        }

        if (plan.EstimateCanonicalMilliseconds is { } completeQuote)
        {
            double? quoted = completeQuote(roots);
            familyComplete = quoted is >= 0 && double.IsFinite(quoted.Value);
            if (familyComplete)
            {
                familyPipelineMs = quoted!.Value;
                missing.Clear(); // Standalone logical-stage prices do not describe this allocation.
            }
            else missing.Add("CompleteAllocationQuoteUnavailable:" + plan.CompleteQuoteEvidence);
            if (plan.EstimatedTerminalSurvival is double terminalSurvival && terminalSurvival is >= 0 and <= 1)
                population = roots * terminalSurvival;
            else
            {
                population = double.NaN;
                missing.Add("CompleteAllocationTerminalSurvivalUnavailable");
            }
        }

        double? exactTailMs = null;
        if (familyComplete && double.IsFinite(population))
        {
            if (population <= 0d)
            {
                exactTailMs = 0d;
            }
            else if (liveExactMsPerAttempt is > 0d && double.IsFinite(liveExactMsPerAttempt.Value))
            {
                exactTailMs = population * liveExactMsPerAttempt.Value / Math.Max(1, request.WorkerCount);
            }
            else if (exactTiming is { Usable: true })
            {
                exactTailMs = population * exactTiming.AverageExactMsPerAttempt / Math.Max(1, request.WorkerCount);
            }
            else
            {
                missing.Add("ProductionExactHardwareEvidenceUnavailable:" + exactFamily);
            }
        }

        bool setupKnown = plan.EstimateCanonicalMilliseconds is null ||
            plan.EstimatedSetupMilliseconds is >= 0 && double.IsFinite(plan.EstimatedSetupMilliseconds.Value);
        if (!setupKnown) missing.Add("CompleteAllocationSetupUnavailable");
        double? total = familyComplete && exactTailMs.HasValue && setupKnown
            ? Math.Max(familyPipelineMs, exactTailMs.Value) + (plan.EstimatedSetupMilliseconds ?? 0d)
            : null;
        return new HorizonProjection(
            total,
            familyComplete ? familyPipelineMs : null,
            exactTailMs,
            stages,
            Distinct(missing));
    }

    private static FamilySearchEtaProjectionV1 Build(
        FamilySearchEtaStatus status,
        double? probability,
        string probabilityEvidence,
        FamilyExecutionPlan plan,
        int targetCount,
        long scanCount,
        long? firstMeanRoots,
        long? firstP99Roots,
        long? targetMeanRoots,
        double? firstMeanMs,
        double? firstP99Ms,
        double? targetMeanMs,
        double? scanMs,
        double? expectedMatchesInScan,
        double? targetProbabilityInScan,
        double? targetFamilyMs,
        double? targetExactMs,
        IReadOnlyList<FamilyEtaStageProjection> targetStages,
        IEnumerable<string> missing) => new(
            status, probability, probabilityEvidence, plan, targetCount, scanCount,
            firstMeanRoots, firstP99Roots, targetMeanRoots,
            firstMeanMs, firstP99Ms, targetMeanMs, scanMs,
            expectedMatchesInScan, targetProbabilityInScan,
            targetFamilyMs, targetExactMs, targetStages, Distinct(missing));

    private static bool TryCeil(double roots, out long value)
    {
        value = 0L;
        if (!(roots > 0d) || !double.IsFinite(roots) || roots > long.MaxValue) return false;
        value = Math.Max(1L, (long)Math.Ceiling(roots));
        return true;
    }

    private static bool TryRootsForAtLeastOneConfidence(double probability, double confidence, out long value)
    {
        value = 0L;
        if (!(probability > 0d && probability <= 1d) || !(confidence > 0d && confidence < 1d)) return false;
        if (probability >= 1d) { value = 1L; return true; }
        double roots = Math.Log(1d - confidence) / double.LogP1(-probability);
        return TryCeil(roots, out value);
    }

    private static string[] Distinct(IEnumerable<string> values) => values
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
}

internal static class FamilyExactTimingDomains
{
    internal static SearchVerificationFamily Resolve(ExactSearchExecutionRequest request) =>
        SearchPredictabilityVerificationStore.ResolveFamily(QueryDomains(request));

    internal static ExactTimingDomain[] QueryDomains(ExactSearchExecutionRequest request)
    {
        var domains = new List<ExactTimingDomain>(4);
        if (request.Evaluation.HasNeowConstraints) domains.Add(ExactTimingDomain.Neow);
        if (request.Evaluation.RequiresRelicSequenceDomain) domains.Add(ExactTimingDomain.Relic);
        if (request.Evaluation.RequiresWorldDomain) domains.Add(ExactTimingDomain.WorldEvent);
        if (request.Evaluation.RequiresNormalCombatRewardDomain) domains.Add(ExactTimingDomain.CombatReward);
        return domains.ToArray();
    }
}
