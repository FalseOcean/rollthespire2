using RolltheSpire2.Search.FamilyExecution;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Search.Runtime;

public interface IProductionSearchSession : IAsyncDisposable
{
    Task Completion { get; }
    void Cancel();
    bool TryReadCandidate(out SearchCandidate? candidate);
    SearchProgressSnapshot GetProgress();
    SearchDiagnosticSummarySnapshot GetDiagnosticSummary();
    ulong GetSafeNextOrdinal();
    bool TryGetSafeNextOrdinal(out ulong nextOrdinal);
}

/// <summary>
/// Complete Search lifecycle: root ranges, Filter allocation/batches, Predictor-backed
/// Exact validation, cancellation, ordered cursor commits and result publication.
/// </summary>
public sealed class ProductionSearchSession : IProductionSearchSession
{
    internal const int ExactOnlyMaximumBatchSize = 8192;
    private const int TerminalHandoffCapacity = 1;
    private const int TerminalOutstandingLimit = TerminalHandoffCapacity + 1;
    private const int LiveBatchStateLimit = TerminalHandoffCapacity + 2;
    private readonly ExactSearchExecutionRequest _plan;
    private readonly FamilyExecutionPlan _executionPlan;
    private readonly FamilySearchEtaProjectionV1 _etaProjection;
    private readonly IRuntimeProfile _profile;
    private readonly IReadOnlyList<IFamilyInvocation> _families;
    private readonly Channel<SearchCandidate> _candidates;
    private readonly Channel<FamilyTerminalBatch> _terminalHandoff;
    private readonly FamilyExecutionContext _executionContext;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IRuntimePredictionDiagnosticSink? _diagnosticSink;
    private readonly string _traceSingleCandidateSeed;
    private readonly int _executionWindowSize;
    private readonly ConcurrentDictionary<string, long> _dispositions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _representativeSeeds = new(StringComparer.Ordinal);
    private readonly object _safeCursorGate = new();
    private readonly Task _completion;
    private long _scanned;
    private int _matches;
    private int _state = (int)SearchRunState.Running;
    private int _lastDisposition = (int)SearchDisposition.NoMatch;
    private int _userCancellationRequested;
    private int _targetReached;
    private int _activeExactBatchNumber;
    private int _overlapObserved;
    private long _familyBatchesProduced;
    private long _exactBatchesStarted;
    private long _exactBatchesCompleted;
    private long _familyExactOverlapCount;
    private long _familyPipelineWallTicks;
    private long _familyToTerminalEnqueueTicks;
    private long _terminalOfferToExactStartTicks;
    private long _exactWorkTicks;
    private long _cursorCommitTicks;
    private long _exactTimingSamples;
    private long _terminalSurvivorCount;
    private long _completionTrackingCount;
    private long _zeroSurvivorFastPathCount;
    private long _exactAttempts;
    private long _exactAggregateStopwatchTicks;
    private int _maxTerminalOutstanding;
    private int _maxPipelineLookahead;
    private int _orderedConsumptionViolationCount;
    private int _cursorContinuityViolationCount;
    private int _boundedQueueViolationCount;
    private string _failureCode = string.Empty;
    private Exception? _exactFailure;
    private int _loggedFault;
    private readonly HashSet<Exception> _loggedExceptions = new(ReferenceEqualityComparer.Instance);
    private SearchCandidate? _firstResult, _lastResult;
    private void LogFault(Exception exception, string phase)
    {
        if(!_loggedExceptions.Add(exception.GetBaseException())) { RuntimeLog.TryBackgroundWarning($"searchFaultTransition=true;phase={phase};canonicalFaultAlreadyRecorded=true"); return; }
        if(Interlocked.Exchange(ref _loggedFault, 1)!=0)
        {
            RuntimeLog.TryBackgroundWarning($"searchAdditionalFault=true;phase={phase};exceptionJson=" + RuntimeLog.SafeJson(exception.ToString()));
            OperationalFileLog.Flush(); return;
        }
        RuntimeLog.Fault($"searchFault=true;phase={phase};plan={_executionPlan.PlanId};start={_plan.CanonicalStartSeed};safeCursor={_safeNextOrdinal};context={_plan.SnapshotFingerprint};familyRevisions={string.Join('|',_families.Select(f=>f.ConditionPerformance.PhysicalImplementationRevision))}", exception);
    }
    private readonly Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult> _exactEvaluator;
    private string _firstFailureCode = string.Empty;
    private string _firstFailureSeed = string.Empty;
    private ulong _safeNextOrdinal;
    private bool _safeCursorInitialized;

    internal ProductionSearchSession(
        ExactSearchExecutionRequest plan,
        FamilyExecutionPlan executionPlan,
        FamilySearchEtaProjectionV1 etaProjection,
        int executionWindowSize,
        int queueCapacity,
        IRuntimePredictionDiagnosticSink? diagnosticSink,
        string traceSingleCandidateSeed,
        bool failOnPhysicalRecovery = false,
        Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult>? exactEvaluator = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _exactEvaluator = exactEvaluator ?? ProductionExactSearchEvaluator.Evaluate;
        _executionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        _etaProjection = etaProjection ?? throw new ArgumentNullException(nameof(etaProjection));
        _profile = RuntimeProfileRegistry.Select(plan.Detection);
        _families = executionPlan.OrderedFamilies;
        var calibrationCoverage = new HashSet<string>();
        foreach(var family in _families)
        {
            if(family is FamilyCpuExecution cpu)cpu.BindCalibrationCoverage(calibrationCoverage);
            calibrationCoverage.UnionWith(family.Coverage);
        }
        _executionContext = new FamilyExecutionContext(requiresGpu: _families.Any(family => family.ConditionPerformance.UsesGpu), failOnPhysicalRecovery);
        if (executionWindowSize <= 0) throw new ArgumentOutOfRangeException(nameof(executionWindowSize));
        _executionWindowSize = executionWindowSize;
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        _diagnosticSink = diagnosticSink;
        _traceSingleCandidateSeed = traceSingleCandidateSeed?.Trim() ?? string.Empty;
        _candidates = Channel.CreateBounded<SearchCandidate>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _terminalHandoff = Channel.CreateBounded<FamilyTerminalBatch>(new BoundedChannelOptions(TerminalHandoffCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _completion = Task.Run(RunAsync);
    }

    public Task Completion => _completion;
    public IReadOnlyList<string> FamilyIds => _families.Select(family => family.FamilyId).ToArray();
    internal FamilyExecutionPlan ExecutionPlan => _executionPlan;
    internal FamilySearchEtaProjectionV1 EtaProjection => _etaProjection;

    // The selected plan and its offline timing/probability evidence are frozen for
    // this session. The UI may separately project frozen rarity with its rolling
    // completed-root wall-rate; it never changes this predicted Cost/ETA snapshot.
    internal long GetLiveEtaEvidenceRevision() => 0;

    internal FamilySearchEtaProjectionV1 GetEtaProjection(bool allowLivePerformance = true) => _etaProjection;

    public void Cancel()
    {
        Interlocked.Exchange(ref _userCancellationRequested, 1);
        _lifetime.Cancel();
    }

    public bool TryReadCandidate(out SearchCandidate? candidate) => _candidates.Reader.TryRead(out candidate);

    private long _scanFirstCompletedTimestamp, _scanLastCompletedTimestamp, _scanAfterFirstRoots;
    public SearchProgressSnapshot GetProgress()
    {
        double elapsed = _clock.Elapsed.TotalSeconds;
        long scanned = Interlocked.Read(ref _scanned);
        return new SearchProgressSnapshot(
            (SearchRunState)Volatile.Read(ref _state), scanned, Volatile.Read(ref _matches),
            _plan.ScanCount, _plan.TargetMatchCount, elapsed, elapsed <= 0d ? 0d : scanned / elapsed,
            (SearchDisposition)Volatile.Read(ref _lastDisposition), Volatile.Read(ref _failureCode) ?? string.Empty,
            Volatile.Read(ref _userCancellationRequested) != 0)
        {
            ObservedScanningSeconds = ElapsedMilliseconds(Math.Max(0,Interlocked.Read(ref _scanLastCompletedTimestamp)-Interlocked.Read(ref _scanFirstCompletedTimestamp)))/1000,
            ObservedScanningRoots = Interlocked.Read(ref _scanAfterFirstRoots)
        };
    }

    public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => new(
        new Dictionary<string, long>(_dispositions, StringComparer.Ordinal),
        new Dictionary<string, long>(_failures, StringComparer.Ordinal),
        new Dictionary<string, string>(_representativeSeeds, StringComparer.Ordinal),
        Volatile.Read(ref _firstFailureCode) ?? string.Empty,
        Volatile.Read(ref _firstFailureSeed) ?? string.Empty);

    public ulong GetSafeNextOrdinal()
    {
        lock (_safeCursorGate) return _safeCursorInitialized ? _safeNextOrdinal : 0UL;
    }

    public bool TryGetSafeNextOrdinal(out ulong nextOrdinal)
    {
        lock (_safeCursorGate)
        {
            nextOrdinal = _safeNextOrdinal;
            return _safeCursorInitialized;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        try { await _completion.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            if (!VisibleSeedCandidateCodec.TryParseOrdinal(_profile, _plan.CanonicalStartSeed, out ulong startOrdinal, out _, out string issue))
                throw new InvalidOperationException("StartSeedOrdinalRejected:" + issue);

            SetSafeCursor(startOrdinal);
            RuntimeLog.TryBackgroundInfo(
                $"familyExecutionStarted=true;spine=FamilyExecution;families={_families.Count};" +
                $"familyIds={string.Join(",", _families.Select(family => family.FamilyId))};abi=ABI1-BatchLocalLogicalOrdinal;" +
                $"start={_plan.CanonicalStartSeed};count={_plan.ScanCount};workers={_plan.WorkerCount};" +
                $"executionWindowSize={_executionWindowSize}");
            RuntimeLog.TryBackgroundInfo("familyPlannerDecision=true;" + _executionPlan.FormatSummary());
            RuntimeLog.TryBackgroundInfo(_etaProjection.FormatSummary() + ";phase=SelectedProductionPlan");
            var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
            foreach (FamilyExecutionPlanStage stage in _executionPlan.Stages)
            {
                IFamilyInvocation family = stage.Family;
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
                passedCoverage.UnionWith(stage.Coverage);
            }

            Task exactConsumer = ConsumeExactBatchesAsync(_lifetime.Token);
            Task familyProducer = ProduceFamilyBatchesAsync(startOrdinal, _lifetime.Token);
            await Task.WhenAll(familyProducer, exactConsumer).ConfigureAwait(false);

            Volatile.Write(ref _state,
                Volatile.Read(ref _userCancellationRequested) != 0
                    ? (int)SearchRunState.Cancelled
                    : (int)SearchRunState.Completed);
        }
        // Parallel.ForEachAsync may surface cancellation when an in-flight Exact
        // computation also failed. Cancellation must not erase a real fault.
        catch (Exception) when (Volatile.Read(ref _exactFailure) is not null)
        {
            Exception failure = Volatile.Read(ref _exactFailure)!;
            Volatile.Write(ref _failureCode, "FamilyExecutionFault:" + failure.GetType().Name + ":" + failure.Message);
            Volatile.Write(ref _state, (int)SearchRunState.Faulted);
            LogFault(failure, "Exact");
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _targetReached) != 0)
        {
            Volatile.Write(ref _state, (int)SearchRunState.Completed);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _state, (int)SearchRunState.Cancelled);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _failureCode, "FamilyExecutionFault:" + ex.GetType().Name + ":" + ex.Message);
            Volatile.Write(ref _state, (int)SearchRunState.Faulted);
            LogFault(ex, "Execution");
        }
        finally
        {
            _terminalHandoff.Writer.TryComplete();
            bool physicalRecoveryContaminated = false;
            foreach (IFamilyInvocation family in _families)
            {
                try { await family.DisposeAsync(_executionContext).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    // Every selected allocation owns cleanup, including ordinary and new
                    // bounded allocations. Cleanup failure cannot report a successful session.
                    if ((SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted)
                        Volatile.Write(ref _failureCode, "FamilyCleanupFault:" + ex.Message);
                    Volatile.Write(ref _state, (int)SearchRunState.Faulted);
                    physicalRecoveryContaminated = true;
                    LogFault(ex, "FamilyCleanup:" + family.FamilyId);
                    RuntimeLog.TryBackgroundWarning(
                        $"familyResourceDisposeFailed=true;family={family.GetType().Name};error={ex.GetType().Name}:{ex.Message}");
                }
                try
                {
                    FamilyPerformanceObservation observation = family.CapturePerformanceObservation();
                    if (string.Equals(observation.EligibilityEvidence, "PhysicalFailureOrRecovery", StringComparison.Ordinal))
                        physicalRecoveryContaminated = true;
                    RuntimeLog.TryBackgroundInfo(
                        $"familyPerformanceObservation=true;family={family.FamilyId};revision={family.ConditionPerformance.PhysicalImplementationRevision};gpu={family.ConditionPerformance.UsesGpu};input={observation.InputCandidates};output={observation.OutputCandidates};canonicalMs={observation.PerformanceBoundaryMs};legacyPeakAuthority=false");
                }
                catch (Exception ex)
                {
                    RuntimeLog.TryBackgroundWarning(
                        $"familyPerformanceObservationFailed=true;family={family.FamilyId};" +
                        $"error={ex.GetType().Name}:{ex.Message}");
                }
            }
            long exactAttempts = Interlocked.Read(ref _exactAttempts);
            double exactAggregateMs = ElapsedMilliseconds(Interlocked.Read(ref _exactAggregateStopwatchTicks));
            bool exactTimingSubmitted = false;
            // Empty queries return seeds but do not measure normal Exact work.
            if (RolltheSpire2.Search.Selectivity.ProbabilitySemanticProjection.From(
                    RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(_plan)).ActiveDomains().Any() &&
                !physicalRecoveryContaminated && exactAttempts > 0 && exactAggregateMs > 0d)
            {
                SearchPredictabilityVerificationStore.SubmitExactTimingSample(
                    FamilyExactTimingDomains.QueryDomains(_plan),
                    exactAttempts,
                    exactAggregateMs,
                    $"FamilyExecutionExactTiming;state={(SearchRunState)Volatile.Read(ref _state)};" +
                    "acceptanceIgnored=true;runtimeSurvivalAuthority=false");
                exactTimingSubmitted = true;
            }
            RuntimeLog.TryBackgroundInfo(
                $"familyExactTimingObservation=true;submitted={exactTimingSubmitted.ToString().ToLowerInvariant()};" +
                $"exactAttempts={exactAttempts};exactAggregateMs={F(exactAggregateMs)};" +
                $"physicalRecoveryContaminated={physicalRecoveryContaminated.ToString().ToLowerInvariant()};" +
                "timingOnly=true;acceptanceIgnored=true;survivalAuthority=false;diskIoOnWorker=false");
            try { await _executionContext.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                if ((SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted)
                    Volatile.Write(ref _failureCode, "FamilyContextCleanupFault:" + ex.Message);
                Volatile.Write(ref _state, (int)SearchRunState.Faulted);
                RuntimeLog.TryBackgroundWarning(
                    $"familyExecutionContextDisposeFailed=true;error={ex.GetType().Name}:{ex.Message}");
                LogFault(ex, "OwnerCleanup");
            }
            foreach(var family in _families)
                try { family.LogExecutionEvidence(); } catch(Exception ex) { RuntimeLog.TryBackgroundWarning("executionEvidenceFailed=" + ex.GetType().Name); }
            RuntimeLog.TryBackgroundInfo($"searchExactOutcome=true;attempts={exactAttempts};evaluatedMatch={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Match))};evaluatedReject={_dispositions.GetValueOrDefault(nameof(SearchDisposition.NoMatch))};evaluatedUnknown={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Unknown))};publishedMatches={_matches};firstResult={_firstResult?.Seed};lastResult={_lastResult?.Seed};canonicalFaultRecorded={_loggedFault!=0}");
            object? ResultEvidence(SearchCandidate? candidate) => candidate is null ? null : new { candidate.Seed, candidate.MatchedRouteIds, candidate.Witnesses };
            RuntimeLog.TryBackgroundInfo("searchResultSamples=true;scope=BoundedFirstLastPublished;resultJson=" + RuntimeLog.SafeJson(new { first=ResultEvidence(_firstResult),last=ReferenceEquals(_firstResult,_lastResult)?null:ResultEvidence(_lastResult) }));
            if (!physicalRecoveryContaminated && (SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted)
            {
                GpuCostCalibration.Submit(_families.SelectMany(f => f.CostPeaks));
                CpuCostCalibration.Submit(_families.OfType<FamilyCpuExecution>().SelectMany(f=>f.LocalPeaks));
            }
            _candidates.Writer.TryComplete();
            RuntimeLog.TryBackgroundInfo(
                $"familyAsyncSummary=true;gpuOwnerStable={_executionContext.GpuOwnerStable};" +
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
                $"familyExactOverlapObserved={Volatile.Read(ref _overlapObserved) != 0};" +
                $"familyExactOverlapCount={Interlocked.Read(ref _familyExactOverlapCount)};" +
                $"sessionElapsedMs={F(_clock.Elapsed.TotalMilliseconds)};" +
                $"searchCandidatesPerSecond={F(CandidatesPerSecond(Interlocked.Read(ref _scanned), _clock.Elapsed.TotalMilliseconds))};" +
                $"familyPipelineWallMs={F(ElapsedMilliseconds(Interlocked.Read(ref _familyPipelineWallTicks)))};" +
                "familyPipelineWallIncludesLazySetup=true;familyPipelineWallIncludesStageLogging=true;" +
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
                $"scanned={Interlocked.Read(ref _scanned)};matches={Volatile.Read(ref _matches)};" +
                $"safeNextOrdinal={GetSafeNextOrdinal()}");
            OperationalFileLog.Flush();
        }
    }

    private async Task ProduceFamilyBatchesAsync(ulong startOrdinal, CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        try
        {
            long remaining = _plan.ScanCount;
            ulong batchBase = startOrdinal;
            int batchNumber = 0;
            while (remaining > 0 && Volatile.Read(ref _targetReached) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int nextBatchNumber = checked(batchNumber + 1);
                int overlappingExactBatch = Volatile.Read(ref _activeExactBatchNumber);
                if (overlappingExactBatch != 0)
                {
                    Interlocked.Exchange(ref _overlapObserved, 1);
                    Interlocked.Increment(ref _familyExactOverlapCount);
                }
                int pipelineLookahead = checked(nextBatchNumber - (int)Interlocked.Read(ref _exactBatchesStarted));
                UpdateMaximum(ref _maxPipelineLookahead, pipelineLookahead);
                if (pipelineLookahead > LiveBatchStateLimit)
                    RecordBoundedQueueViolation("PipelineLookaheadExceeded", pipelineLookahead);

                // Window sizing is coordinator/physical policy. SearchBatch itself
                // remains only BatchBase + BatchCandidateCount and owns no 2^24 rule.
                int count = (int)Math.Min(remaining, _executionWindowSize);
                var batch = new SearchBatch(batchBase, count);
                var observation = new FamilyObservationWindow(batch, _plan);
                long familyPipelineStarted = Stopwatch.GetTimestamp();
                FamilyCandidateSet survivors;
                try
                {
                    survivors = await LinearFamilyPipeline.FilterAsync(
                        _executionContext, observation, _families, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - familyPipelineStarted);
                }

                long familyCompletedTimestamp = Stopwatch.GetTimestamp();
                if(nextBatchNumber==1) RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FirstRootBatchCompleted;ownerThreadId={_executionContext.GpuOwnerThreadId};sinceSessionMs={_clock.Elapsed.TotalMilliseconds:F4};roots={count};survivors={survivors.Count};plan={_executionPlan.PlanId}");
                // Presentation-only interval between completed root batches: excludes lazy GPU setup
                // and the GPU first batch. CPU may include its first completed pure scanning batch.
                // Includes subsequent submit/handoff/backpressure elapsed time.
                if(nextBatchNumber==1)
                {
                    bool cpuOnly=_families.Count>0 && !_families.Any(f=>f.ConditionPerformance.UsesGpu);
                    Interlocked.Exchange(ref _scanFirstCompletedTimestamp,cpuOnly?familyPipelineStarted:familyCompletedTimestamp);
                    if(cpuOnly)Interlocked.Add(ref _scanAfterFirstRoots,count);
                }
                else Interlocked.Add(ref _scanAfterFirstRoots,count);
                Interlocked.Exchange(ref _scanLastCompletedTimestamp,familyCompletedTimestamp);
                var terminalBatch = new FamilyTerminalBatch(nextBatchNumber, survivors, familyCompletedTimestamp);
                // Channel ownership excludes a batch already transferred to Exact.
                int terminalOutstanding = _terminalHandoff.Reader.Count + 1;
                UpdateMaximum(ref _maxTerminalOutstanding, terminalOutstanding);
                if (terminalOutstanding > TerminalOutstandingLimit)
                    RecordBoundedQueueViolation("TerminalOutstandingExceeded", terminalOutstanding);
                terminalBatch.MarkTerminalOffered();
                await _terminalHandoff.Writer.WriteAsync(terminalBatch, cancellationToken).ConfigureAwait(false);
                Interlocked.Add(
                    ref _familyToTerminalEnqueueTicks,
                    Stopwatch.GetTimestamp() - terminalBatch.FamilyCompletedTimestamp);
                Interlocked.Increment(ref _familyBatchesProduced);
                batchNumber = nextBatchNumber;
                remaining -= count;
                batchBase = checked(batchBase + (ulong)count);
            }
        }
        catch (Exception ex)
        {
            completionError = ex;
            _lifetime.Cancel();
            throw;
        }
        finally
        {
            _terminalHandoff.Writer.TryComplete(completionError);
        }
    }

    private async Task ConsumeExactBatchesAsync(CancellationToken cancellationToken)
    {
        int expectedBatchNumber = 1;
        try
        {
            await foreach (FamilyTerminalBatch terminalBatch in
                           _terminalHandoff.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (terminalBatch.BatchNumber != expectedBatchNumber)
                {
                    int violationCount = Interlocked.Increment(ref _orderedConsumptionViolationCount);
                    if (violationCount == 1)
                    {
                        RuntimeLog.TryBackgroundWarning(
                            $"familyOrderedConsumptionViolation=true;expectedBatch={expectedBatchNumber};" +
                            $"actualBatch={terminalBatch.BatchNumber}");
                    }
                    throw new InvalidOperationException(
                        $"FamilyTerminalBatchOrderMismatch:expected={expectedBatchNumber};actual={terminalBatch.BatchNumber}");
                }

                FamilyCandidateSet candidates = terminalBatch.Candidates;
                long exactStartTimestamp = Stopwatch.GetTimestamp();
                Interlocked.Add(
                    ref _terminalOfferToExactStartTicks,
                    exactStartTimestamp - terminalBatch.TerminalOfferTimestamp);
                Volatile.Write(ref _activeExactBatchNumber, terminalBatch.BatchNumber);
                Interlocked.Increment(ref _exactBatchesStarted);
                try
                {
                    await EvaluateExactTerminalAsync(
                        candidates,
                        exactStartTimestamp,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _activeExactBatchNumber, 0);
                }

                Interlocked.Increment(ref _exactBatchesCompleted);
                expectedBatchNumber++;
            }
        }
        catch
        {
            _lifetime.Cancel();
            throw;
        }
        finally
        {
            Volatile.Write(ref _activeExactBatchNumber, 0);
        }
    }

    private async ValueTask EvaluateExactTerminalAsync(
        FamilyCandidateSet candidates,
        long exactStartTimestamp,
        CancellationToken cancellationToken)
    {
        int survivorCount = candidates.Count;
        Interlocked.Add(ref _terminalSurvivorCount, survivorCount);
        if (!candidates.IsDense)
            Interlocked.Add(ref _scanned, candidates.Batch.BatchCandidateCount - survivorCount);

        if (survivorCount == 0)
        {
            Interlocked.Increment(ref _zeroSurvivorFastPathCount);
            long zeroSurvivorExactCompletedTimestamp = Stopwatch.GetTimestamp();
            Interlocked.Add(ref _exactWorkTicks, zeroSurvivorExactCompletedTimestamp - exactStartTimestamp);
            long cursorCommitStartedTimestamp = Stopwatch.GetTimestamp();
            SetSafeCursor(checked(candidates.Batch.BatchBase + (ulong)candidates.Batch.BatchCandidateCount));
            Interlocked.Add(
                ref _cursorCommitTicks,
                Stopwatch.GetTimestamp() - cursorCommitStartedTimestamp);
            Interlocked.Increment(ref _exactTimingSamples);
            return;
        }

        // Family-rejected roots are already terminally complete. ABI1 is strictly
        // ordered, so only actual survivors need completion state: the first
        // unfinished survivor is the continuous resume boundary.
        var completed = new bool[survivorCount];
        Interlocked.Add(ref _completionTrackingCount, survivorCount);
        ReadOnlyMemory<ulong> compactAbi1 = candidates.IsDense ? default : candidates.ExportAbi1();
        long exactWorkCompletedTimestamp = 0;
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, survivorCount),
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = _plan.WorkerCount },
                async (survivorIndex, token) =>
                {
                    if (Volatile.Read(ref _targetReached) != 0) return;
                    ulong logicalOrdinal = candidates.IsDense
                        ? (ulong)survivorIndex
                        : compactAbi1.Span[survivorIndex];
                    await EvaluateCandidateAsync(candidates.Batch, logicalOrdinal, token).ConfigureAwait(false);
                    Volatile.Write(ref completed[survivorIndex], true);
                }).ConfigureAwait(false);
            exactWorkCompletedTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            if (exactWorkCompletedTimestamp == 0)
                exactWorkCompletedTimestamp = Stopwatch.GetTimestamp();
            Interlocked.Add(ref _exactWorkTicks, exactWorkCompletedTimestamp - exactStartTimestamp);
            long cursorCommitStartedTimestamp = Stopwatch.GetTimestamp();
            SetSafeCursor(DeriveSafeNextOrdinal(candidates, completed));
            Interlocked.Add(
                ref _cursorCommitTicks,
                Stopwatch.GetTimestamp() - cursorCommitStartedTimestamp);
            Interlocked.Increment(ref _exactTimingSamples);
        }
    }

    private async ValueTask EvaluateCandidateAsync(SearchBatch batch, ulong logicalOrdinal, CancellationToken cancellationToken)
    {
        ulong globalCandidate = batch.GlobalCandidate(logicalOrdinal);
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(_profile, globalCandidate);
        ulong rootHash = _profile.ComputeRootSeed(seed);
        SearchQueryEvaluation evaluation;
        ProductionExactSearchResult exact;
        if (!TrustedRootHashInput.TryBindCanonicalSeed(
                _profile, rootHash, seed, out TrustedRootHashInput input, out string bindIssue))
        {
            evaluation = SearchQueryEvaluation.Unknown("TrustedRootHashBindingRejected:" + bindIssue);
            exact = new ProductionExactSearchResult(null, null, _plan.Authority, evaluation, evaluation.FailureCode);
        }
        else
        {
            Interlocked.Increment(ref _exactAttempts);
            long exactAttemptStarted = Stopwatch.GetTimestamp();
            try
            {
                exact = _exactEvaluator(_plan, input);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _exactFailure, ex, null);
                throw;
            }
            finally
            {
                Interlocked.Add(
                    ref _exactAggregateStopwatchTicks,
                    Math.Max(0L, Stopwatch.GetTimestamp() - exactAttemptStarted));
            }
            evaluation = exact.Evaluation;
        }

        Interlocked.Increment(ref _scanned);
        Volatile.Write(ref _lastDisposition, (int)evaluation.Disposition);
        RecordOutcome(seed, evaluation.Disposition, evaluation.FailureCode);
        if (!exact.IsMatch) return;

        int matchNumber = TryReserveMatch();
        if (matchNumber == 0) return;
        string snapshotFingerprint = string.Join("|", new[]
        {
            _plan.SnapshotFingerprint,
            exact.Authority.EffectSnapshotFingerprint,
            exact.Authority.WorldSnapshotFingerprint
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var output = new SearchCandidate(
            seed, _plan.ProfileId, _plan.CharacterKey, _plan.Ascension, snapshotFingerprint,
            exact.Request!, exact.Document!, exact.Authority,
            evaluation.Evidence, evaluation.MatchedRouteIds, evaluation.Witnesses);
        await _candidates.Writer.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        Interlocked.CompareExchange(ref _firstResult, output, null); Volatile.Write(ref _lastResult, output);

        if (!string.IsNullOrWhiteSpace(_traceSingleCandidateSeed) &&
            string.Equals(seed, _traceSingleCandidateSeed, StringComparison.OrdinalIgnoreCase) &&
            _diagnosticSink is not null)
        {
            RuntimePredictionDiagnosticReporter.EmitAnalysis(
                _diagnosticSink, exact.Request!, exact.Document!, _plan.Detection.DisplayVersion,
                forceTrace: true, source: "FamilyExecutionExactTrace");
        }

        if (matchNumber >= _plan.TargetMatchCount)
        {
            Interlocked.Exchange(ref _targetReached, 1);
            _lifetime.Cancel();
        }
    }

    internal static ulong DeriveSafeNextOrdinal(FamilyCandidateSet candidates, bool[] completed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.Length != candidates.Count)
            throw new ArgumentException("Completion state must correspond one-to-one with terminal survivors.", nameof(completed));

        int survivorIndex = 0;
        while (survivorIndex < completed.Length && Volatile.Read(ref completed[survivorIndex]))
            survivorIndex++;
        if (survivorIndex == completed.Length)
            return checked(candidates.Batch.BatchBase + (ulong)candidates.Batch.BatchCandidateCount);

        ulong logicalOrdinal = candidates.IsDense
            ? (ulong)survivorIndex
            : candidates.ExportAbi1().Span[survivorIndex];
        return checked(candidates.Batch.BatchBase + logicalOrdinal);
    }

    private int TryReserveMatch()
    {
        while (true)
        {
            int current = Volatile.Read(ref _matches);
            if (current >= _plan.TargetMatchCount) return 0;
            if (Interlocked.CompareExchange(ref _matches, current + 1, current) == current) return current + 1;
        }
    }

    private void RecordOutcome(string seed, SearchDisposition disposition, string failureCode)
    {
        _dispositions.AddOrUpdate(disposition.ToString(), 1, (_, count) => count + 1);
        if (string.IsNullOrWhiteSpace(failureCode)) return;
        _failures.AddOrUpdate(failureCode, 1, (_, count) => count + 1);
        _representativeSeeds.TryAdd(failureCode, seed);
        if (string.IsNullOrWhiteSpace(Volatile.Read(ref _firstFailureCode)))
        {
            Interlocked.CompareExchange(ref _firstFailureCode, failureCode, string.Empty);
            Interlocked.CompareExchange(ref _firstFailureSeed, seed, string.Empty);
        }
    }

    private void SetSafeCursor(ulong nextOrdinal)
    {
        lock (_safeCursorGate)
        {
            if (_safeCursorInitialized && nextOrdinal < _safeNextOrdinal)
            {
                int violationCount = Interlocked.Increment(ref _cursorContinuityViolationCount);
                if (violationCount == 1)
                {
                    RuntimeLog.TryBackgroundWarning(
                        $"familyCursorContinuityViolation=true;currentSafeNextOrdinal={_safeNextOrdinal};" +
                        $"attemptedSafeNextOrdinal={nextOrdinal}");
                }
                return;
            }
            if (!_safeCursorInitialized || nextOrdinal > _safeNextOrdinal)
                _safeNextOrdinal = nextOrdinal;
            _safeCursorInitialized = true;
        }
    }

    private void RecordBoundedQueueViolation(string issue, int observedDepth)
    {
        int violationCount = Interlocked.Increment(ref _boundedQueueViolationCount);
        if (violationCount != 1) return;
        RuntimeLog.TryBackgroundWarning(
            $"familyBoundedQueueViolation=true;issue={issue};observedDepth={observedDepth};" +
            $"terminalHandoffCapacity={TerminalHandoffCapacity};" +
            $"terminalOutstandingLimit={TerminalOutstandingLimit};terminalOutstandingDefinition=QueuedPlusCurrentOffer;" +
            $"liveBatchStateLimit={LiveBatchStateLimit}");
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static double ElapsedMilliseconds(long stopwatchTicks) =>
        stopwatchTicks * 1000d / Stopwatch.Frequency;

    private static double CandidatesPerSecond(long candidates, double milliseconds) =>
        milliseconds <= 0d ? 0d : candidates * 1000d / milliseconds;

    private static string F(double value) =>
        value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private sealed class FamilyTerminalBatch
    {
        public FamilyTerminalBatch(
            int batchNumber,
            FamilyCandidateSet candidates,
            long familyCompletedTimestamp)
        {
            BatchNumber = batchNumber;
            Candidates = candidates;
            FamilyCompletedTimestamp = familyCompletedTimestamp;
        }

        public int BatchNumber { get; }
        public FamilyCandidateSet Candidates { get; }
        public long FamilyCompletedTimestamp { get; }
        public long TerminalOfferTimestamp { get; private set; }

        public void MarkTerminalOffered() => TerminalOfferTimestamp = Stopwatch.GetTimestamp();
    }
}
