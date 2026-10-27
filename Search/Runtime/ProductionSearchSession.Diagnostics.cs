using System.Collections.Concurrent;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

public sealed partial class ProductionSearchSession
{
    // Counters belong to the compiled request, which may be reused by Host
    // warmup/session runs. Take before/after snapshots for per-session deltas.
    internal RolltheSpire2.Core.Rewards.CombatRewardPoolPreparationEvidence? CaptureRewardPoolPreparationEvidence() =>
        _plan.RewardPoolPreparation?.Capture();

    private readonly IRuntimePredictionDiagnosticSink? _diagnosticSink;
    private readonly string _traceSingleCandidateSeed;
    private readonly ConcurrentDictionary<string, long> _dispositions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _representativeSeeds = new(StringComparer.Ordinal);
    private int _validationIncompleteReported;
    private int _loggedFault;
    private readonly HashSet<Exception> _loggedExceptions = new(ReferenceEqualityComparer.Instance);
    private SearchCandidate? _firstResult, _lastResult;
    private long _nonMatchSampleCount;
    private sealed record FirstFailure(string Code, string Seed);
    private FirstFailure? _firstFailure;
    private readonly ConcurrentDictionary<string, byte> _executionWarnings = new(StringComparer.Ordinal);
    private string _cleanupIssue = "";

    public SearchDiagnosticSummarySnapshot GetDiagnosticSummary()
    {
        var first = Volatile.Read(ref _firstFailure);
        return new(
            new Dictionary<string, long>(_dispositions, StringComparer.Ordinal),
            new Dictionary<string, long>(_failures, StringComparer.Ordinal),
            new Dictionary<string, string>(_representativeSeeds, StringComparer.Ordinal),
            first?.Code ?? string.Empty, first?.Seed ?? string.Empty);
    }

    private void LogFault(Exception exception, string phase)
    {
        if(!_loggedExceptions.Add(exception.GetBaseException())) { RuntimeLog.TryBackgroundWarning($"searchFaultTransition=true;phase={phase};canonicalFaultAlreadyRecorded=true"); return; }
        Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception,
            "SearchExecutionFailed", nameof(ProductionSearchSession), phase, "issues.search_failed",
            correlationId: $"session={RuntimeLog.SessionId};plan={ExecutionPlan.PlanId};start={_plan.CanonicalStartSeed};safeCursor={GetSafeNextOrdinal()};context={_plan.SnapshotFingerprint}");
        if(Interlocked.Exchange(ref _loggedFault, 1)!=0)
        {
            RuntimeLog.TryBackgroundWarning($"searchAdditionalFault=true;phase={phase};recordedInIssueLedger=true");
            OperationalFileLog.Flush(); return;
        }
        RuntimeLog.TryBackgroundWarning($"searchFault=true;phase={phase};plan={ExecutionPlan.PlanId};familyRevisions={string.Join('|',_families.Select(f=>f.ConditionPerformance.PhysicalImplementationRevision))};recordedInIssueLedger=true");
    }
    private void RecordDiagnosticFailure(Exception exception, string stage)
    {
        try { Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception,
            "SearchDiagnosticFailed", nameof(ProductionSearchSession), stage, "issues.diagnostics_failed",
            Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning,
            correlationId: $"session={RuntimeLog.SessionId};plan={ExecutionPlan.PlanId};start={_plan.CanonicalStartSeed}"); }
        catch (Exception reportingFailure)
        {
            RuntimeLog.TryBackgroundWarning($"searchDiagnosticFailed=true;stage={stage};original={exception};reportingFailure={reportingFailure}");
        }
    }

    private void RecordOutcome(string seed, SearchDisposition disposition, string failureCode)
    {
        _dispositions.AddOrUpdate(disposition.ToString(), 1, (_, count) => count + 1);
        if (string.IsNullOrWhiteSpace(failureCode)) return;
        _failures.AddOrUpdate(failureCode, 1, (_, count) => count + 1);
        _representativeSeeds.TryAdd(failureCode, seed);
        if (Volatile.Read(ref _firstFailure) is null)
            Interlocked.CompareExchange(ref _firstFailure, new(failureCode, seed), null);
    }

    private void RecordCandidateFailure(ulong ordinal, string seed, Exception exception)
    {
        const string stage = "CandidateCheck";
        string code = "CandidateCheckFailed:" + exception.GetType().Name;
        _failures.AddOrUpdate(code, 1, (_, count) => count + 1);
        _representativeSeeds.TryAdd(code, seed);
        Interlocked.CompareExchange(ref _firstFailure, new(code, seed), null);
        TryDiagnostic(() => Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, code,
            nameof(ProductionSearchSession), stage, "issues.search_failed",
            correlationId: $"seed={seed};ordinal={ordinal};query={_plan.CompiledSearch.SemanticFingerprint};context={_plan.SnapshotFingerprint};plan={ExecutionPlan.PlanId};pending=true"), stage);
    }

    private void RecordCleanupFailure(Exception exception, string stage)
    {
        // The owner is disposed once and is never reused. Retain the calculation
        // outcome and report cleanup separately, including its original chain.
        _cleanupIssue += (string.IsNullOrEmpty(_cleanupIssue) ? "" : "\n") + stage + ": " + exception;
        TryDiagnostic(() => LogFault(exception, stage), stage);
    }

    private string[] BatchExecutionWarnings(SearchBatch batch)
    {
        IEnumerable<string> warnings = _executionContext.CaptureFilterIsolations(batch).Select(DescribeIsolation);
        var recovery = Volatile.Read(ref _gpuCreationCpuRecovery);
        if (recovery is not null && batch.BatchBase >= recovery.FirstOrdinal) warnings = warnings.Append(recovery.Warning);
        return warnings.ToArray();
    }

    private string[] CaptureExecutionWarnings()
    {
        IEnumerable<string> warnings = _executionWarnings.Keys;
        var payload = _recovery.CapturePendingPayload();
        if (payload.AccountingFailure is not null)
            warnings = warnings.Append($"SearchRecoveryPayloadAccountingIncomplete:countedCompactJsonBytes={payload.Bytes};" +
                "unmeasuredPending=true;admissionUsesCountedBytesOnly=true");
        return warnings.Order(StringComparer.Ordinal).ToArray();
    }

    private static string DescribeIsolation(FamilyFilterIsolation isolation) =>
        $"FilterIsolated:{isolation.FamilyId};stage={isolation.Stage};unfiltered={string.Join(',', isolation.UnfilteredCoverage)};" +
        $"sourceOrdinals={string.Join(',', isolation.FailedGlobalOrdinals)};reason={isolation.Error.GetBaseException().Message};" +
        $"finalValidationSkipped={isolation.FinalValidationSkipped}";

    private void LogStartEvidence()
    {
        TryDiagnostic(() => {
        RuntimeLog.TryBackgroundInfo(
            $"familyExecutionStarted=true;spine=FamilyExecution;families={_families.Count};" +
            $"familyIds={string.Join(",", _families.Select(family => family.FamilyId))};abi=ABI1-BatchLocalLogicalOrdinal;" +
            $"start={_plan.CanonicalStartSeed};count={_plan.ScanCount};workers={_plan.WorkerCount};" +
            $"executionWindowSize={_executionWindowSize};skipExactValidation={_plan.RunOptions.SkipExactValidation}");
        RuntimeLog.TryBackgroundInfo("familyPlannerDecision=true;" + _executionPlan.FormatSummary());
        RuntimeLog.TryBackgroundInfo("searchSupportScopeJson=" + RuntimeLog.SafeJson(_plan.Authority.SupportScope));
        RuntimeLog.TryBackgroundInfo(_etaProjection.FormatSummary() + ";phase=SelectedProductionPlan");
        }, "SessionStartEvidence");
        var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
        foreach (FamilyExecutionPlanStage stage in _executionPlan.Stages)
        {
            IFamilyInvocation family = stage.Family;
            try
            {
                RuntimeLog.TryBackgroundInfo(
                    "familyAnalyticalCostProjection=true;authority=FamilyOperationLedgerV1;" +
                    family.AnalyticalCost.FormatSummary() + ";hardwareTimingUsed=false;plannerDecision=false");
                RuntimeLog.TryBackgroundInfo(
                    "familySurvivalProjection=true;" + family.ResolveSurvival(passedCoverage).FormatSummary() +
                    ";runtimeObservationUsed=false;plannerDecision=false");
                if (family.ResolveExpectedFilteringCost(passedCoverage) is { } expectedFilteringCost)
                    RuntimeLog.TryBackgroundInfo(
                        "familyExpectedFilteringCost=true;" + expectedFilteringCost.FormatSummary() +
                        ";probabilityAuthority=QueryImmutableRuntimeAuthority;runtimeObservationUsed=false;" +
                        "hardwareTimingUsed=false;plannerDecision=false");
                RuntimeLog.TryBackgroundInfo(
                    "familyConditionPerformance=true;" + stage.ConditionPerformance.FormatSummary() +
                    $";selectedInputMode={(stage.CompactAbi1Input ? "CompactAbi1" : "Dense")}" +
                    ";runtimeRegressionUsed=false;analyticalCostUsed=false;plannerDecision=false");
                RuntimeLog.TryBackgroundInfo(
                    "familyPerformanceReference=true;" + stage.Performance.FormatSummary() +
                    ";developerBootstrapIsLocalAuthority=false");
            }
            catch (Exception ex) { RecordDiagnosticFailure(ex, "PlanEvidence:" + family.FamilyId); }
            passedCoverage.UnionWith(stage.Coverage);
        }
    }

    private void LogResultEvidence(long exactAttempts)
    {
        RuntimeLog.TryBackgroundInfo($"searchExactOutcome=true;attempts={exactAttempts};evaluatedMatch={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Match))};evaluatedReject={_dispositions.GetValueOrDefault(nameof(SearchDisposition.NoMatch))};evaluatedUnknown={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Unknown))};evaluatedUnsupported={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Unsupported))};publishedMatches={_confirmedCandidates};publishedUnverified={_unverifiedCandidates};publishedTotal={_matches};firstResult={_firstResult?.Seed};lastResult={_lastResult?.Seed};canonicalFaultRecorded={_loggedFault!=0}");
        RuntimeLog.TryBackgroundInfo("searchExactDiagnostics=true;summaryJson=" + RuntimeLog.SafeJson(GetDiagnosticSummary()));
        object? ResultEvidence(SearchCandidate? candidate) => candidate is null ? null : new { candidate.Seed, candidate.ValidationDisposition, candidate.ValidationIssue, candidate.SupportScope, candidate.IsUnverified, candidate.IsUnfiltered, candidate.MatchedRouteIds, candidate.Witnesses };
        RuntimeLog.TryBackgroundInfo("searchResultSamples=true;scope=BoundedFirstLastPublished;resultJson=" + RuntimeLog.SafeJson(new { first=ResultEvidence(_firstResult),last=ReferenceEquals(_firstResult,_lastResult)?null:ResultEvidence(_lastResult) }));
    }

    private void LogCompletionEvidence()
    {
        if (_smallCandidateExactPolicy is not null)
            RuntimeLog.TryBackgroundInfo("smallCandidateExactSummary=true;scope=PublicPipeline;" +
                _smallCandidateExactPolicy.GetSummary());
        RuntimeLog.TryBackgroundInfo(
            $"familyAsyncSummary=true;gpuOwnerStable={_executionContext.GpuOwnerStable};" +
            $"activePlan={ExecutionPlan.PlanId};activeExecutionWindowSize={ExecutionWindowSize};gpuCreationCpuRecovery={Volatile.Read(ref _gpuCreationCpuRecovery) is not null};" +
            $"gpuOwnerThreadId={_executionContext.GpuOwnerThreadId};" +
            $"orderedExactConsumption={Volatile.Read(ref _orderedConsumptionViolationCount) == 0};" +
            $"orderedCursorCommit={Volatile.Read(ref _cursorContinuityViolationCount) == 0};" +
            $"cursorContinuityViolationCount={Volatile.Read(ref _cursorContinuityViolationCount)};" +
            $"orderedConsumptionViolationCount={Volatile.Read(ref _orderedConsumptionViolationCount)};" +
            $"terminalHandoffCapacity={TerminalHandoffCapacity};" +
            $"terminalOutstandingLimit={TerminalOutstandingLimit};terminalOutstandingDefinition=QueuedPlusCurrentOffer;" +
            $"maxTerminalOutstanding={Volatile.Read(ref _maxTerminalOutstanding)};" +
            $"liveBatchStateLimit={LiveBatchStateLimit};" +
            $"maxPipelineLookahead={Volatile.Read(ref _maxPipelineLookahead)};" +
            $"boundedQueueViolationCount={Volatile.Read(ref _boundedQueueViolationCount)};" +
            $"familyBatchesProduced={Interlocked.Read(ref _familyBatchesProduced)};" +
            $"exactBatchesStarted={Interlocked.Read(ref _exactBatchesStarted)};" +
            $"exactBatchesCompleted={Interlocked.Read(ref _exactBatchesCompleted)};" +
            $"familyExactOverlapObserved={Interlocked.Read(ref _familyExactOverlapCount) > 0};" +
            $"familyExactOverlapCount={Interlocked.Read(ref _familyExactOverlapCount)};" +
            $"cpuWorkerBudget={_plan.WorkerCount};peakActiveCpuWorkers={_executionContext.PeakCpuWorkers};" +
            $"sessionElapsedMs={F(_clock.Elapsed.TotalMilliseconds)};" +
            $"searchCandidatesPerSecond={F(CandidatesPerSecond(Interlocked.Read(ref _scanned), _clock.Elapsed.TotalMilliseconds))};" +
            $"familyPipelineWallMs={F(ElapsedMilliseconds(Interlocked.Read(ref _familyPipelineWallTicks)))};" +
            "familyPipelineWallIncludesLazySetup=true;familyPipelineWallIncludesStageLogging=true;" +
            $"rootBatchPairGroups={_rootBatchPairGroups};rootBatchPairAdmissionWaitMs={F(ElapsedMilliseconds(Interlocked.Read(ref _rootBatchPairAdmissionWaitTicks)))};" +
            $"rootBatchPairEnabled={FamilyDenseBatchGroupingPolicy.Enabled};rootBatchPairDefaultPreference={FamilyDenseBatchGroupingPolicy.DefaultPreference};" +
            "familyPipelineWallExcludesRootPairAdmissionWait=true;rootBatchPairSharedWaitChargedOnce=true;" +
            $"familyToTerminalEnqueueMs={F(ElapsedMilliseconds(Interlocked.Read(ref _familyToTerminalEnqueueTicks)))};" +
            $"terminalOfferToExactStartMs={F(ElapsedMilliseconds(Interlocked.Read(ref _terminalOfferToExactStartTicks)))};" +
            $"exactWorkMs={F(ElapsedMilliseconds(Interlocked.Read(ref _exactWorkTicks)))};" +
            $"cursorCommitMs={F(ElapsedMilliseconds(Interlocked.Read(ref _cursorCommitTicks)))};" +
            $"exactTimingSamples={Interlocked.Read(ref _exactTimingSamples)};" +
            $"terminalSurvivorCount={Interlocked.Read(ref _terminalSurvivorCount)};" +
            $"completionTrackingCount={Interlocked.Read(ref _completionTrackingCount)};" +
            $"zeroSurvivorFastPath={Interlocked.Read(ref _zeroSurvivorFastPathCount)};" +
            "legacyFallback=false;unboundedAsyncState=false");
        RuntimeLog.TryBackgroundInfo(
            $"familyExecutionCompleted=true;state={(SearchRunState)Volatile.Read(ref _state)};" +
            $"scanned={Interlocked.Read(ref _scanned)};matches={Volatile.Read(ref _confirmedCandidates)};unverified={Volatile.Read(ref _unverifiedCandidates)};delivered={Volatile.Read(ref _matches)};" +
            $"safeNextOrdinal={GetSafeNextOrdinal()}");
        TryDiagnostic(OperationalFileLog.Flush, "FinalLogFlush");
    }

    private void RecordRejectedCandidate(string seed, ProductionExactSearchResult exact)
    {
        // Bounded evidence is emitted immediately, so an ongoing search/feedback export retains it.
        long sampled = Interlocked.Increment(ref _nonMatchSampleCount);
        if (sampled <= 6) TryDiagnostic(() => ExactRejectionDiagnostics.Log(_plan, seed, exact), "RejectionEvidence");
        if ((sampled & (sampled - 1)) == 0)
            RuntimeLog.TryBackgroundInfo("searchExactDiagnostics=true;phase=Progress;summaryJson=" + RuntimeLog.SafeJson(GetDiagnosticSummary()));
    }

    private void TraceCandidate(string seed, ProductionExactSearchResult exact)
    {
        if (!string.IsNullOrWhiteSpace(_traceSingleCandidateSeed) &&
            string.Equals(seed, _traceSingleCandidateSeed, StringComparison.OrdinalIgnoreCase) &&
            _diagnosticSink is not null)
        {
            TryDiagnostic(() => RuntimePredictionDiagnosticReporter.EmitAnalysis(
                _diagnosticSink, exact.Request!, exact.Document!, _plan.Detection.DisplayVersion,
                forceTrace: true, source: "FamilyExecutionExactTrace"), "CandidateTrace");
        }
    }

    private void RecordPublishedCandidate(SearchCandidate output, int classificationCount)
    {
        Interlocked.CompareExchange(ref _firstResult, output, null); Volatile.Write(ref _lastResult, output);
        if (output.IsUnverified && classificationCount <= 6)
            RuntimeLog.TryBackgroundInfo($"searchUnverifiedCandidatePublished=true;seed={output.Seed};validationDisposition={output.ValidationDisposition};confirmed=false;unfiltered={output.IsUnfiltered};validationIssueJson=" + RuntimeLog.SafeJson(output.ValidationIssue));
        if (output.ValidationDisposition is SearchDisposition.Unknown or SearchDisposition.Unsupported &&
            Interlocked.CompareExchange(ref _validationIncompleteReported, 1, 0) == 0)
            TryDiagnostic(() => Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("SearchValidationIncomplete",
                nameof(ProductionSearchSession), "CandidatePublished", "issues.validation_incomplete",
                $"seed={output.Seed};disposition={output.ValidationDisposition};reason={output.ValidationIssue};sourceAuthority={output.Authority.SourceAuthority};supportScopeJson={RuntimeLog.SafeJson(output.SupportScope)};plan={ExecutionPlan.PlanId}"), "IncompleteValidationNotice");
    }

    private void TryDiagnostic(Action action, string stage)
    {
        try { action(); }
        catch (Exception exception) { RecordDiagnosticFailure(exception, stage); }
    }
}
