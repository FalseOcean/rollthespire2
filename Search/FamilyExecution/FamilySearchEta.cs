using System.Globalization;
using System.Text.Json;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

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
    double? Survival,
    string PhysicalImplementationRevision,
    double? ReferenceCandidatesPerSecond,
    FamilyPerformanceEvidenceSource EvidenceSource,
    double? SteadyStateMs)
{
    internal string CostEvidence { get; init; } = "";
}

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
    internal string ExactTimingImplementationRevision { get; init; } = "";
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
                $"survival={Maybe(stage.Survival)},physicalRevision={San(stage.PhysicalImplementationRevision)}," +
                $"referenceCps={Maybe(stage.ReferenceCandidatesPerSecond)},source={stage.EvidenceSource}," +
                $"steadyMs={Maybe(stage.SteadyStateMs)},costEvidence={San(stage.CostEvidence)}]"));
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
               $"exactTimingImplementationRevision={San(ExactTimingImplementationRevision.Length == 0 ? "Legacy" : ExactTimingImplementationRevision)};" +
               $"targetStages={stages};missingEvidence={(MissingEvidence.Count == 0 ? "none" : string.Join('|', MissingEvidence.Select(San)))};" +
               "steadyStateComposition=max(FamilyPipeline,ExactTail)+min(FamilyPipeline,ExactTail)/ExpectedWindowCount;sameBatchFamiliesOrdered=true;" +
               "crossBatchFamilyExactOverlap=true;startupIncluded=false;" +
               "costBasis=FamilyComputationPlusSeparateTransportOrApplicableCompleteCall;geometryFallback=ExplicitReferenceApproximation;" +
               "earlyExactEstimate=ExpectedPopulationPerRootWindow;runtimeSurvivalLearning=false;searchAdmissionAffected=false";
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
        double? SearchMs,
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
        return FamilyAuxiliaryEvidence.Read(nameof(FamilySearchEtaProjectorV1), "Project",
            () => ProjectCore(request, plan, livePerformanceByPhysicalRevision, liveExactMsPerAttempt) with
            { ExactTimingImplementationRevision = FamilyExactTimingDomains.ImplementationRevision(request) },
            Build(FamilySearchEtaStatus.Unavailable, null, "EtaProjectionFailed", plan,
                Math.Max(1, request.TargetMatchCount), request.ScanCount,
                null, null, null, null, null, null, null, null, null, null, null, [], ["EtaProjectionFailed"]),
            request.SnapshotFingerprint);
    }

    private static FamilySearchEtaProjectionV1 ProjectCore(
        ExactSearchExecutionRequest request, FamilyExecutionPlan plan,
        IReadOnlyDictionary<string, double>? livePerformanceByPhysicalRevision, double? liveExactMsPerAttempt)
    {
        int targetCount = Math.Max(1, request.TargetMatchCount);
        JointSelectivityResult joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        SearchVerificationFamily exactFamily = FamilyExactTimingDomains.Resolve(request);
        SearchExactTimingEvidence? exactTiming = FamilyAuxiliaryEvidence.Read<SearchExactTimingEvidence?>(
            nameof(FamilySearchEtaProjectorV1), "ExactTiming", () => SearchPredictabilityVerificationStore.TryGetExactTiming(
                exactFamily, FamilyExactTimingDomains.ImplementationRevision(request)), null,
            request.SnapshotFingerprint);

        HorizonProjection scan = ProjectHorizon(
            request.ScanCount, request, plan, exactTiming, exactFamily,
            livePerformanceByPhysicalRevision, liveExactMsPerAttempt);
        var missing = new List<string>(scan.MissingEvidence);
        if (joint.ExactlyImpossible && joint.Probability == 0d)
        {
            return Build(
                FamilySearchEtaStatus.Impossible, 0d, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.SearchMs, 0d, 0d,
                null, null, [], missing);
        }

        if (!joint.JointlyPriced || joint.Probability is not double probability)
        {
            missing.Add("AcceptedResultProbabilityUnavailable:" + joint.EvidenceCode);
            missing.AddRange(joint.UnknownComponents.Select(item => "QueryProbabilityComponentUnavailable:" + item));
            return Build(
                FamilySearchEtaStatus.Unavailable, null, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.SearchMs, null, null,
                null, null, [], missing);
        }
        if (!(probability > 0d && probability <= 1d) || !double.IsFinite(probability))
        {
            missing.Add("AcceptedResultProbabilityInvalid");
            return Build(
                FamilySearchEtaStatus.Unavailable, null, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.SearchMs, null, null,
                null, null, [], missing);
        }

        if (!TryCeil(SearchPredictabilityMath.ExpectedRootsToFirst(probability), out long firstMeanRoots) ||
            !TryRootsForAtLeastOneConfidence(probability, FirstResultP99Confidence, out long firstP99Roots) ||
            !TryCeil(Math.Min(request.ScanCount, SearchPredictabilityMath.ExpectedRootsToTarget(probability, targetCount)), out long targetMeanRoots))
        {
            missing.Add("QueryProbabilityRootHorizonOverflow");
            return Build(
                FamilySearchEtaStatus.Unavailable, probability, joint.EvidenceCode, plan, targetCount, request.ScanCount,
                null, null, null, null, null, null, scan.SearchMs,
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
        bool searchReady = SearchTime(target).HasValue && SearchTime(firstMean).HasValue && SearchTime(firstP99).HasValue;
        FamilySearchEtaStatus status = searchReady
            ? FamilySearchEtaStatus.Ready
            : distinctMissing.Any(e => e.StartsWith("FamilyHardwarePerformanceUnavailable:",StringComparison.Ordinal) ||
                e.StartsWith("FamilySurvivalUnavailable:",StringComparison.Ordinal))
                ? FamilySearchEtaStatus.Unavailable : FamilySearchEtaStatus.Calibrating;
        return Build(
            status, probability, joint.EvidenceCode, plan, targetCount, request.ScanCount,
            firstMeanRoots, firstP99Roots, targetMeanRoots,
            firstMean.SearchMs, firstP99.SearchMs, target.SearchMs, scan.SearchMs,
            probability * request.ScanCount,
            SearchPredictabilityMath.ProbabilityAtLeastTarget(probability, request.ScanCount, targetCount),
            target.FamilyPipelineMs, target.ExactTailMs, target.Stages, distinctMissing) with
        {
            FirstResultSearchMeanMs = SearchTime(firstMean),
            FirstResultSearchP99Ms = SearchTime(firstP99),
            TargetSearchMeanMs = SearchTime(target)
        };
    }

    private static double? SearchTime(HorizonProjection horizon) => horizon.SearchMs;

    private sealed class StageTotal(FamilyExecutionPlanStage stage, double? survival)
    {
        internal double Inputs, Outputs;
        internal double? Milliseconds = 0d;
        internal FamilyPerformanceEvidenceSource Source = stage.Performance.EvidenceSource;
        internal readonly List<string> Evidence = [];
        internal FamilyEtaStageProjection Projection() => new(stage.Family.FamilyId,
            stage.CompactAbi1Input, Inputs, Outputs, survival,
            stage.Performance.PhysicalImplementationRevision,
            Milliseconds is > 0 ? Inputs * 1000d / Milliseconds.Value : null,
            Source, Milliseconds) { CostEvidence = string.Join(';', Evidence.Distinct(StringComparer.Ordinal)) };
    }

    // No new plan is selected here. A private transport wrapper retains the
    // exact unit order and references selected before it was constructed.
    internal static IReadOnlyList<FamilyExecutionPlanStage> EstimationStages(FamilyExecutionPlan plan) =>
        plan.Stages.SelectMany(stage => stage.Family is AutomaticPrivateSerialInvocation { EstimationStages.Count: > 0 } serial
            ? serial.EstimationStages : new[] { stage }).ToArray();

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
        double familyPipelineMs = 0d;
        bool familyComplete = true;
        bool populationComplete = true;
        double exactPopulation = 0d;
        int executionWindow = FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies);
        var selectedStages = EstimationStages(plan);
        bool privateReference = plan.Stages.Any(stage => stage.Family is AutomaticPrivateSerialInvocation { EstimationStages.Count: > 0 });
        var costSnapshot = FamilyAuxiliaryEvidence.Read<GpuCostSnapshot?>(nameof(FamilySearchEtaProjectorV1),
            "TimingCostSnapshot", GpuCostCalibration.Capture, null);
        var totals = selectedStages.Select(stage => new StageTotal(stage,
            stage.Survival ?? (selectedStages.Count == 1 ? plan.EstimatedTerminalSurvival : null))).ToArray();
        bool earlyExact = plan.AllowSmallCandidateExact && !request.RunOptions.SkipExactValidation;

        // At most two fragment shapes: repeated full root windows and one real
        // tail. The <=8 decision uses each window's modeled mean, not the whole
        // search horizon; runtime still decides only from actual integer counts.
        void AddFragment(int rootPopulation, double repetitions)
        {
            if (rootPopulation <= 0 || repetitions <= 0) return;
            double population = rootPopulation, inputFraction = 1d;
            var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
            bool handoff = false;
            for (int index = 0; index < selectedStages.Count; index++)
            {
                FamilyExecutionPlanStage stage = selectedStages[index];
                StageTotal total = totals[index];
                string fragment = $"RootFragment={rootPopulation};Windows={repetitions:G17};";
                handoff |= earlyExact && population is > 0 and <= SmallCandidateExactPolicy.MaximumCandidates;
                if (population == 0 || handoff)
                {
                    total.Evidence.Add(fragment + (population == 0 ? "EmptyStageNotInvoked" :
                        $"ExpectedPopulationEarlyExactApproximation;ExpectedCandidates={population:G17};ActualRuntimeCountOwnsHandoff"));
                    continue;
                }
                double? projectedSurvival = stage.Survival ?? (selectedStages.Count == 1 ? plan.EstimatedTerminalSurvival : null);
                if (projectedSurvival is not double survival || !double.IsFinite(survival) || survival is < 0 or > 1)
                {
                    missing.Add("FamilySurvivalUnavailable:" + stage.Family.FamilyId);
                    familyComplete = populationComplete = false;
                    total.Milliseconds = null;
                    break;
                }
                double output = population * survival;
                double? time;
                string costEvidence;
                if (livePerformanceByPhysicalRevision is not null && livePerformanceByPhysicalRevision.TryGetValue(
                        stage.Performance.PhysicalImplementationRevision, out double liveReference) &&
                    liveReference > 0d && double.IsFinite(liveReference))
                {
                    time = FamilyPerformanceProjectionMath.ExpectedMilliseconds(population, liveReference);
                    total.Source = FamilyPerformanceEvidenceSource.Live;
                    costEvidence = "ExistingLivePerformanceReference;TimingOnly;ProbabilityUnchanged";
                }
                else
                {
                    double reach = SearchPredictabilityMath.ProbabilityAtLeastOne(inputFraction, rootPopulation);
                    double inputs = population / reach;
                    var geometry = new FamilyPhysicalQuoteRequest(stage.CompactAbi1Input,
                        privateReference && index < selectedStages.Count - 1, inputs,
                        PrivateInput: privateReference && index > 0);
                    var price = FamilyPhysicalQuote.CompleteCall(stage.Family, geometry, costSnapshot, survival,
                        passedCoverage, rootPopulation, inputFraction, includeInvokeOverhead: !privateReference || index == 0,
                        privateInvocation: privateReference, correlationId: request.SnapshotFingerprint);
                    time = price?.Milliseconds;
                    total.Source = price?.Source ?? FamilyPerformanceEvidenceSource.Reference;
                    costEvidence = price?.Evidence ??
                        "SelectedUnitComputationUnavailable;NoBodyWorkerOrDeviceSubstitution;NoCompleteCallSubstitution";
                }
                if (!time.HasValue || !double.IsFinite(time.Value) || time.Value < 0)
                {
                    missing.Add("FamilyHardwarePerformanceUnavailable:" + stage.Family.FamilyId + ":" + costEvidence);
                    familyComplete = false;
                    total.Milliseconds = null;
                }
                else
                {
                    double repeatedTime = repetitions * time.Value;
                    familyPipelineMs += repeatedTime;
                    total.Milliseconds += repeatedTime;
                }
                total.Inputs += repetitions * population;
                total.Outputs += repetitions * output;
                total.Evidence.Add(fragment + (privateReference
                    ? "SelectedFamilyComputationWithSeparatePrivateTransferEnvelope;PrivateTransportOverheadNotMeasured;NoPrivateChainQuote;" : "") + costEvidence);
                population = output;
                inputFraction *= survival;
                passedCoverage.UnionWith(stage.Coverage);
            }
            exactPopulation += repetitions * population;
        }
        double fullWindows = Math.Floor(roots / executionWindow);
        AddFragment(executionWindow, fullWindows);
        AddFragment((int)(roots - fullWindows * executionWindow), 1d);

        double? exactTailMs = null;
        if (request.RunOptions.SkipExactValidation) exactTailMs = 0d;
        else if (populationComplete && double.IsFinite(exactPopulation))
        {
            double effectiveExactWorkers = Math.Min(Math.Max(1, request.WorkerCount), Math.Max(1, exactPopulation));
            if (exactPopulation <= 0d)
            {
                exactTailMs = 0d;
            }
            else if (liveExactMsPerAttempt is > 0d && double.IsFinite(liveExactMsPerAttempt.Value))
            {
                exactTailMs = exactPopulation * liveExactMsPerAttempt.Value / effectiveExactWorkers;
            }
            else if (exactTiming is { Usable: true })
            {
                exactTailMs = exactPopulation * exactTiming.AverageExactMsPerAttempt / effectiveExactWorkers;
            }
            else
            {
                missing.Add("ProductionExactHardwareEvidenceUnavailable:" + exactFamily +
                    ":implementation=" + (FamilyExactTimingDomains.ImplementationRevision(request) is { Length: > 0 } revision ? revision : "Legacy"));
            }
        }

        double? search = null;
        if (familyComplete && exactTailMs.HasValue)
        {
            double windows = Math.Max(1, Math.Ceiling(roots /
                FamilyExecutionCoordinator.ResolveExecutionWindowSize(request, plan.OrderedFamilies)));
            // The single window must finish filtering before its Exact work can
            // start. Later windows overlap the bounded producer and consumer.
            search = Math.Max(familyPipelineMs, exactTailMs.Value) +
                Math.Min(familyPipelineMs, exactTailMs.Value) / windows;
        }
        return new HorizonProjection(
            search,
            familyComplete ? familyPipelineMs : null,
            exactTailMs,
            totals.Select(total => total.Projection()).ToArray(),
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
        // long.MaxValue rounds to 2^63 as a double; that value cannot be cast
        // back to a positive long. Reject it instead of wrapping to a tiny ETA.
        if (!(roots > 0d) || !double.IsFinite(roots) || roots >= 9223372036854775808d) return false;
        value = Math.Max(1L, (long)Math.Ceiling(roots));
        return true;
    }

    private static bool TryRootsForAtLeastOneConfidence(double probability, double confidence, out long value)
    {
        value = 0L;
        if (!(probability > 0d && probability <= 1d) || !(confidence > 0d && confidence < 1d)) return false;
        if (probability >= 1d) { value = 1L; return true; }
        double roots = Math.Log(1d - confidence) / SearchPredictabilityMath.LogMissProbability(probability);
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
    internal static string ImplementationRevision(ExactSearchExecutionRequest request)
    {
        // Fast C has its own unchanged physical prices. Only the changed solo
        // Exact reward path needs a new timing key; keep other v1 entries usable.
        if (request.CompiledSearch.Context.Party is not null || request.Authority.PlayersCount != 1 ||
            !request.Evaluation.RequiresNormalCombatRewardDomain) return "";
        bool fixedPools = request.RewardPoolPreparation?.Pools is not null;
        bool identityReuse = request.ProfileId == Compatibility.RuntimeProfileId.Beta111 &&
            request.ExactOptions.OpeningReuse && request.PartyOpeningChoices is null &&
            request.Authority.PlayerSlotIndex == 0 && request.Authority.IsBeta111NeowIdentityAuthorityExact;
        return !fixedPools && !identityReuse ? "" :
            $"ProductionExact.SoloReward.20261027.v1.{request.ProfileId}.FixedPools{(fixedPools ? 1 : 0)}.IdentityReuse{(identityReuse ? 1 : 0)}";
    }

    private static bool MapOnly(SearchQuery query)
    {
        if ((query.StandardMaps.Count == 0 && query.StandardMapRoutes.Count == 0) || query.Players.Count is not (0 or 2 or 3 or 4) ||
            query.Players.Any(p => !p.Offers.IsEmpty || p.SelectedOption is not null || p.Results.Count > 0 ||
                JsonSerializer.Serialize(p.Conditions) != JsonSerializer.Serialize(SearchQuery.Empty))) return false;
        SearchQuery withoutMap = query with { StandardMaps = [], StandardMapRoutes = [], Players = [] };
        return JsonSerializer.Serialize(withoutMap) == JsonSerializer.Serialize(SearchQuery.Empty);
    }

    internal static bool HasQueryWork(SearchQuery query)
    {
        // A seat and its eligibility premises alone do not measure normal Exact work.
        // Inspect the complete query so personal and map-only predicates are retained.
        return query.Players.Any(p => !p.Offers.IsEmpty || p.SelectedOption is not null ||
                p.Results.Count > 0 || HasQueryWork(p.Conditions)) ||
            JsonSerializer.Serialize(query with { Players = [] }) != JsonSerializer.Serialize(SearchQuery.Empty);
    }

    internal static SearchVerificationFamily Resolve(ExactSearchExecutionRequest request)
    {
        if (request.CompiledSearch.Context.Party is not { } party)
        {
            if (request.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0 || request.CompiledSearch.NormalizedQuery.StandardMapRoutes.Count > 0)
                return MapOnly(request.CompiledSearch.NormalizedQuery)
                    ? SearchVerificationFamily.MapOnly
                    : request.Evaluation.RequiresNormalCombatRewardDomain
                        ? SearchVerificationFamily.MapMixedWithReward
                        : SearchVerificationFamily.MapMixedWithoutReward;
            return SearchPredictabilityVerificationStore.ResolveFamily(QueryDomains(request));
        }
        bool map = request.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0 || request.CompiledSearch.NormalizedQuery.StandardMapRoutes.Count > 0;
        bool reward = request.CompiledSearch.NormalizedQuery.Players.Any(p => p.Conditions.HasCombatRewardConstraints);
        return (party.Players.Count, map, reward) switch
        {
            (2, false, false) => SearchVerificationFamily.Party2,
            (3, false, false) => SearchVerificationFamily.Party3,
            (4, false, false) => SearchVerificationFamily.Party4,
            (2, true, false) => SearchVerificationFamily.Party2Map,
            (3, true, false) => SearchVerificationFamily.Party3Map,
            (4, true, false) => SearchVerificationFamily.Party4Map,
            (2, false, true) => SearchVerificationFamily.Party2Reward,
            (3, false, true) => SearchVerificationFamily.Party3Reward,
            (4, false, true) => SearchVerificationFamily.Party4Reward,
            (2, true, true) => SearchVerificationFamily.Party2MapReward,
            (3, true, true) => SearchVerificationFamily.Party3MapReward,
            (4, true, true) => SearchVerificationFamily.Party4MapReward,
            _ => throw new InvalidOperationException("PartyExactTimingPlayerCount")
        };
    }

    internal static ExactTimingDomain[] QueryDomains(ExactSearchExecutionRequest request)
    {
        var domains = new List<ExactTimingDomain>(4);
        if (request.Evaluation.HasNeowConstraints || request.Evaluation.TransformationAggregate is { UsesNeow: true }) domains.Add(ExactTimingDomain.Neow);
        if (request.Evaluation.RequiresRelicSequenceDomain) domains.Add(ExactTimingDomain.Relic);
        if (request.Evaluation.RequiresWorldDomain || request.Evaluation.TransformationAggregate is { UsesEvents: true }) domains.Add(ExactTimingDomain.WorldEvent);
        if (request.Evaluation.RequiresNormalCombatRewardDomain) domains.Add(ExactTimingDomain.CombatReward);
        return domains.ToArray();
    }
}
