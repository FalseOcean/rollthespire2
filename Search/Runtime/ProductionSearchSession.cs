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
    // 合批只改提交方式，Exact 與游標仍逐批處理；流水線上限多留一組。
    private bool _pipelineActive;
    private bool _epochBatchActive;
    private int EffectivePipelineLookaheadLimit => _pipelineActive
        ? LiveBatchStateLimit + 2 * (FamilyBatchGrouping.GroupSize - 1)
        : LiveBatchStateLimit + (FamilyBatchGrouping.GroupSize - 1);
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
    private int _reservedMatches;
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
    private long _batchMergeGroups;
    private long _batchMergeWindows;
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
    private long _rejectionSampleCount;
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
    private readonly Func<int, CancellationToken, ValueTask>? _beforeCandidatePublish;
    private sealed record FirstFailure(string Code, string Seed);
    private FirstFailure? _firstFailure;
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
        Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult>? exactEvaluator = null,
        Func<int, CancellationToken, ValueTask>? beforeCandidatePublish = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _exactEvaluator = exactEvaluator ?? ((request, input) => ProductionExactSearchEvaluator.Evaluate(request, input, _lifetime.Token));
        _beforeCandidatePublish = beforeCandidatePublish;
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
            (SearchRunState)Volatile.Read(ref _state), scanned, _plan.RunOptions.SkipExactValidation ? 0 : Volatile.Read(ref _matches),
            _plan.ScanCount, _plan.TargetMatchCount, elapsed, elapsed <= 0d ? 0d : scanned / elapsed,
            (SearchDisposition)Volatile.Read(ref _lastDisposition), Volatile.Read(ref _failureCode) ?? string.Empty,
            Volatile.Read(ref _userCancellationRequested) != 0)
        {
            UnverifiedCandidateCount = _plan.RunOptions.SkipExactValidation ? Volatile.Read(ref _matches) : 0,
            ObservedScanningSeconds = ElapsedMilliseconds(Math.Max(0,Interlocked.Read(ref _scanLastCompletedTimestamp)-Interlocked.Read(ref _scanFirstCompletedTimestamp)))/1000,
            ObservedScanningRoots = Interlocked.Read(ref _scanAfterFirstRoots)
        };
    }

    public SearchDiagnosticSummarySnapshot GetDiagnosticSummary()
    {
        var first = Volatile.Read(ref _firstFailure);
        return new(
            new Dictionary<string, long>(_dispositions, StringComparer.Ordinal),
            new Dictionary<string, long>(_failures, StringComparer.Ordinal),
            new Dictionary<string, string>(_representativeSeeds, StringComparer.Ordinal),
            first?.Code ?? string.Empty, first?.Seed ?? string.Empty);
    }

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
                $"executionWindowSize={_executionWindowSize};skipExactValidation={_plan.RunOptions.SkipExactValidation}");
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
            if (FamilyExactTimingDomains.HasQueryWork(_plan.CompiledSearch.NormalizedQuery) &&
                !physicalRecoveryContaminated && exactAttempts > 0 && exactAggregateMs > 0d)
            {
                SearchPredictabilityVerificationStore.SubmitExactTimingSample(
                    FamilyExactTimingDomains.Resolve(_plan),
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
            RuntimeLog.TryBackgroundInfo($"searchExactOutcome=true;attempts={exactAttempts};evaluatedMatch={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Match))};evaluatedReject={_dispositions.GetValueOrDefault(nameof(SearchDisposition.NoMatch))};evaluatedUnknown={_dispositions.GetValueOrDefault(nameof(SearchDisposition.Unknown))};publishedMatches={(_plan.RunOptions.SkipExactValidation ? 0 : _matches)};publishedUnverified={(_plan.RunOptions.SkipExactValidation ? _matches : 0)};firstResult={_firstResult?.Seed};lastResult={_lastResult?.Seed};canonicalFaultRecorded={_loggedFault!=0}");
            RuntimeLog.TryBackgroundInfo("searchExactDiagnostics=true;summaryJson=" + RuntimeLog.SafeJson(GetDiagnosticSummary()));
            object? ResultEvidence(SearchCandidate? candidate) => candidate is null ? null : new { candidate.Seed, candidate.IsUnverified, candidate.MatchedRouteIds, candidate.Witnesses };
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
                $"liveBatchStateLimit={EffectivePipelineLookaheadLimit};pipelineActive={_pipelineActive.ToString().ToLowerInvariant()};" +
                $"epochBatchActive={_epochBatchActive.ToString().ToLowerInvariant()};" +
                $"epochBatchCompiled={FamilyPipelineModel.EpochCompiled.ToString().ToLowerInvariant()};" +
                $"pipelineCompiled={FamilyPipelineModel.Compiled.ToString().ToLowerInvariant()};restGpuOwnerThreadId={_executionContext.RestGpuOwnerThreadId};" +
                $"batchMergeGroupSize={FamilyBatchGrouping.GroupSize};" +
                $"batchMergeGroups={Interlocked.Read(ref _batchMergeGroups)};" +
                $"batchMergeWindows={Interlocked.Read(ref _batchMergeWindows)};" +
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
        if (TrySelectEpochBatch(out NeowFamily? epochNeow, out IFamilyEpochInvocation? epochRelic,
                out IFamilyInvocation[]? epochRemaining))
        {
            _epochBatchActive = true;
            await ProduceFamilyBatchesEpochAsync(
                epochNeow!, epochRelic!, epochRemaining!, startOrdinal, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (TrySelectStagePipeline(out NeowFamily? pipelineNeow, out IFamilyInvocation[]? restFamilies) &&
            await _executionContext.TryEnablePipelineAsync(cancellationToken).ConfigureAwait(false))
        {
            _pipelineActive = true;
            await ProduceFamilyBatchesPipelinedAsync(
                pipelineNeow!, restFamilies!, startOrdinal, cancellationToken).ConfigureAwait(false);
            return;
        }
        await ProduceFamilyBatchesCoreAsync(startOrdinal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>雙裝置流水線只接單人 N→R/W/A/E，其餘維持原執行路徑。</summary>
    private bool TrySelectStagePipeline(out NeowFamily? neow, out IFamilyInvocation[]? restFamilies)
    {
        neow = null;
        restFamilies = null;
        if (!FamilyPipelineModel.Compiled || FamilyBatchGrouping.GroupSize < 2) return false;
        if (_families.Count < 2 || _families[0] is not NeowFamily candidate || !candidate.PipelineCapable)
            return false;
        var rest = new IFamilyInvocation[_families.Count - 1];
        for (int index = 1; index < _families.Count; index++)
        {
            IFamilyInvocation family = _families[index];
            // 只放行已接到第二裝置的類別。
            bool routed = family switch
            {
                CapsuleRelicFamily => true,
                RelicFamily => true,
                WorldFamily => true,
                AncientOptionFamily => true,
                EventResultFamily => true,
                _ => false
            };
            if (!routed) return false;
            rest[index - 1] = family;
        }
        neow = candidate;
        restFamilies = rest;
        return true;
    }

    private (List<FamilyObservationWindow> Windows, long Remaining, ulong BatchBase) BuildGroupWindows(
        long remaining, ulong batchBase, int groupSize)
    {
        var windows = new List<FamilyObservationWindow>(groupSize);
        while (windows.Count < groupSize && remaining > 0 &&
               Volatile.Read(ref _targetReached) == 0)
        {
            int groupedCount = (int)Math.Min(remaining, _executionWindowSize);
            windows.Add(new FamilyObservationWindow(new SearchBatch(batchBase, groupedCount), _plan));
            batchBase = checked(batchBase + (ulong)groupedCount);
            remaining -= groupedCount;
        }
        return (windows, remaining, batchBase);
    }

    private static IReadOnlyList<FamilyCandidateSet> BuildDenseInputs(IReadOnlyList<FamilyObservationWindow> windows)
    {
        var inputs = new FamilyCandidateSet[windows.Count];
        for (int index = 0; index < windows.Count; index++)
            inputs[index] = FamilyCandidateSet.Dense(windows[index].Batch);
        return inputs;
    }

    private async Task ProduceFamilyBatchesPipelinedAsync(
        NeowFamily neow,
        IReadOnlyList<IFamilyInvocation> restFamilies,
        ulong startOrdinal,
        CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        int groupSize = FamilyBatchGrouping.GroupSize;
        List<FamilyObservationWindow>? inFlight = null;
        bool inFlightSubmitted = false;
        int inFlightStartBatch = 1;
        List<FamilyObservationWindow>? collected = null;
        IReadOnlyList<FamilyCandidateSet>? collectedNeow = null;
        int collectedStartBatch = 1;
        int nextBatchNumber = 1;
        long remaining = _plan.ScanCount;
        ulong batchBase = startOrdinal;
        try
        {
            (List<FamilyObservationWindow> windows, long nextRemaining, ulong nextBase) =
                BuildGroupWindows(remaining, batchBase, groupSize);
            remaining = nextRemaining;
            batchBase = nextBase;
            if (windows.Count == 0) return;
            long bootstrapStarted = Stopwatch.GetTimestamp();
            try
            {
                await neow.SubmitPipelineGroupAsync(
                    _executionContext, windows, BuildDenseInputs(windows), cancellationToken).ConfigureAwait(false);
            }
            catch (FamilyBatchNotSupportedException ex)
            {
                // 尚未產出批次，可直接退回原路徑。
                _pipelineActive = false;
                _executionContext.DisablePipeline();
                RuntimeLog.TryBackgroundWarning(
                    $"nPipelineFallback=true;reason={ex.Message};fallback=singleDevicePath");
                await ProduceFamilyBatchesCoreAsync(startOrdinal, cancellationToken).ConfigureAwait(false);
                return;
            }
            inFlight = windows;
            inFlightSubmitted = true;
            inFlightStartBatch = nextBatchNumber;
            nextBatchNumber += windows.Count;
            if (windows.Count > 1)
            {
                Interlocked.Increment(ref _batchMergeGroups);
                Interlocked.Add(ref _batchMergeWindows, windows.Count);
            }
            Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - bootstrapStarted);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _targetReached) != 0) break;
                long iterationStarted = Stopwatch.GetTimestamp();

                // 下一組 Neow 在主裝置執行時，處理上一組的下游。
                if (collected is not null)
                {
                    int overlappingExactBatch = Volatile.Read(ref _activeExactBatchNumber);
                    if (overlappingExactBatch != 0)
                    {
                        Interlocked.Exchange(ref _overlapObserved, 1);
                        Interlocked.Increment(ref _familyExactOverlapCount);
                    }
                    int pipelineLookahead = checked(
                        collectedStartBatch + collected.Count - 1 -
                        (int)Interlocked.Read(ref _exactBatchesStarted));
                    UpdateMaximum(ref _maxPipelineLookahead, pipelineLookahead);
                    if (pipelineLookahead > EffectivePipelineLookaheadLimit)
                        RecordBoundedQueueViolation("PipelineLookaheadExceeded", pipelineLookahead);
                    IReadOnlyList<FamilyCandidateSet> restOutputs =
                        await LinearFamilyPipeline.FilterRemainingGroupAsync(
                            _executionContext, collected, restFamilies, collectedNeow!, cancellationToken)
                            .ConfigureAwait(false);
                    long familyCompletedTimestamp = Stopwatch.GetTimestamp();
                    await EnqueuePipelinedGroupAsync(
                        collected, restOutputs, collectedStartBatch, familyCompletedTimestamp, cancellationToken)
                        .ConfigureAwait(false);
                    collected = null;
                    collectedNeow = null;
                }

                // 收回上一輪提交的 Neow 結果。
                if (inFlightSubmitted && inFlight is not null)
                {
                    IReadOnlyList<FamilyCandidateSet> neowOutputs =
                        await neow.CompletePipelineGroupAsync(_executionContext, cancellationToken)
                            .ConfigureAwait(false);
                    collected = inFlight;
                    collectedNeow = neowOutputs;
                    collectedStartBatch = inFlightStartBatch;
                    inFlight = null;
                    inFlightSubmitted = false;
                }

                // 先提交下一組 Neow，與下一輪下游工作重疊。
                if (remaining > 0)
                {
                    (List<FamilyObservationWindow> nextWindows, long restRemaining, ulong restBase) =
                        BuildGroupWindows(remaining, batchBase, groupSize);
                    if (nextWindows.Count > 0)
                    {
                        try
                        {
                            await neow.SubmitPipelineGroupAsync(
                                _executionContext, nextWindows, BuildDenseInputs(nextWindows), cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (FamilyBatchNotSupportedException ex)
                        {
                            throw new InvalidOperationException("NPipelineSubmissionRejected:" + ex.Message, ex);
                        }
                        if (nextWindows.Count > 1)
                        {
                            Interlocked.Increment(ref _batchMergeGroups);
                            Interlocked.Add(ref _batchMergeWindows, nextWindows.Count);
                        }
                        inFlight = nextWindows;
                        inFlightSubmitted = true;
                        inFlightStartBatch = nextBatchNumber;
                        nextBatchNumber += nextWindows.Count;
                    }
                    remaining = restRemaining;
                    batchBase = restBase;
                }

                Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - iterationStarted);
                if (!inFlightSubmitted && collected is null) break;
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
            if (inFlightSubmitted && inFlight is not null) neow.AbortPipelineGroup(_executionContext);
            _terminalHandoff.Writer.TryComplete(completionError);
        }
    }

    private async Task EnqueuePipelinedGroupAsync(
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> survivorsGroup,
        int firstBatchNumber,
        long familyCompletedTimestamp,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < windows.Count; index++)
        {
            int currentBatchNumber = checked(firstBatchNumber + index);
            FamilyCandidateSet survivors = survivorsGroup[index];
            SearchBatch batch = windows[index].Batch;
            int count = batch.BatchCandidateCount;
            if (currentBatchNumber == 1)
            {
                RuntimeLog.TryBackgroundInfo(
                    $"searchStartup=true;phase=FirstRootBatchCompleted;ownerThreadId={_executionContext.GpuOwnerThreadId};" +
                    $"sinceSessionMs={_clock.Elapsed.TotalMilliseconds:F4};roots={count};survivors={survivors.Count};plan={_executionPlan.PlanId}");
                Interlocked.Exchange(ref _scanFirstCompletedTimestamp, familyCompletedTimestamp);
            }
            if (currentBatchNumber != 1) Interlocked.Add(ref _scanAfterFirstRoots, count);
            Interlocked.Exchange(ref _scanLastCompletedTimestamp, familyCompletedTimestamp);
            var terminalBatch = new FamilyTerminalBatch(currentBatchNumber, survivors, familyCompletedTimestamp);
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
        }
    }

    /// <summary>Epoch 只接單人 N→R→W/A/E，其餘維持原執行路徑。</summary>
    private bool TrySelectEpochBatch(out NeowFamily? neow, out IFamilyEpochInvocation? relic,
        out IFamilyInvocation[]? remainingFamilies)
    {
        neow = null;
        relic = null;
        remainingFamilies = null;
        if (!FamilyPipelineModel.EpochCompiled || FamilyBatchGrouping.GroupSize < 2) return false;
        if (_families.Count < 2 || _families[0] is not NeowFamily candidate ||
            candidate is not IFamilyEpochInvocation neowEpoch || !neowEpoch.EpochEligible) return false;
        if (_families[1] is not IFamilyEpochInvocation relicEpoch || !relicEpoch.EpochEligible) return false;
        var rest = new IFamilyInvocation[_families.Count - 2];
        for (int index = 2; index < _families.Count; index++)
        {
            bool routed = _families[index] switch
            {
                WorldFamily => true,
                AncientOptionFamily => true,
                EventResultFamily => true,
                _ => false
            };
            if (!routed) return false;
            rest[index - 2] = _families[index];
        }
        neow = candidate;
        relic = relicEpoch;
        remainingFamilies = rest;
        return true;
    }

    private void CheckPipelineLookahead(int firstBatchNumber, int windowCount)
    {
        int pipelineLookahead = checked(
            firstBatchNumber + windowCount - 1 - (int)Interlocked.Read(ref _exactBatchesStarted));
        UpdateMaximum(ref _maxPipelineLookahead, pipelineLookahead);
        if (pipelineLookahead > EffectivePipelineLookaheadLimit)
            RecordBoundedQueueViolation("PipelineLookaheadExceeded", pipelineLookahead);
    }

    private async Task ProduceFamilyBatchesEpochAsync(NeowFamily neow, IFamilyEpochInvocation relic,
        IReadOnlyList<IFamilyInvocation> remainingFamilies,
        ulong startOrdinal, CancellationToken cancellationToken)
    {
        IFamilyEpochInvocation neowEpoch = neow;
        IFamilyInvocation relicFamily = _families[1];
        Exception? completionError = null;
        int groupSize = FamilyBatchGrouping.GroupSize;
        long remaining = _plan.ScanCount;
        ulong batchBase = startOrdinal;
        int nextBatchNumber = 1;
        List<FamilyObservationWindow>? pendingWindows = null;
        IReadOnlyList<FamilyCandidateSet>? pendingNeow = null;
        int pendingStartBatch = 1;
        try
        {
            try
            {
                await neowEpoch.EnsureEpochExecutorAsync(_executionContext, cancellationToken).ConfigureAwait(false);
                await relic.EnsureEpochExecutorAsync(_executionContext, cancellationToken).ConfigureAwait(false);
            }
            catch (FamilyBatchNotSupportedException ex)
            {
                _epochBatchActive = false;
                RuntimeLog.TryBackgroundWarning(
                    $"nEpochFallback=true;reason={ex.Message};fallback=singleDevicePath");
                await ProduceFamilyBatchesCoreAsync(startOrdinal, cancellationToken).ConfigureAwait(false);
                return;
            }

            while (remaining > 0 && Volatile.Read(ref _targetReached) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (List<FamilyObservationWindow> windows, long restRemaining, ulong restBase) =
                    BuildGroupWindows(remaining, batchBase, groupSize);
                remaining = restRemaining;
                batchBase = restBase;
                if (windows.Count == 0) break;

                // 單窗口不能合批；先清掉待處理 Relic，再逐窗口執行。
                if (windows.Count == 1)
                {
                    long singleStarted = Stopwatch.GetTimestamp();
                    if (pendingWindows is not null && pendingNeow is not null)
                    {
                        IReadOnlyList<FamilyCandidateSet> pendingOutputs =
                            await FlushRelicEpochAsync(relic, pendingWindows, pendingNeow, cancellationToken)
                                .ConfigureAwait(false);
                        await EnqueueWithRemainingAsync(pendingWindows, pendingOutputs, pendingStartBatch,
                            remainingFamilies, cancellationToken).ConfigureAwait(false);
                        pendingWindows = null;
                        pendingNeow = null;
                    }
                    IReadOnlyList<FamilyCandidateSet> singleOutputs =
                        await LinearFamilyPipeline.FilterGroupAsync(
                            _executionContext, windows, _families, cancellationToken).ConfigureAwait(false);
                    await EnqueuePipelinedGroupAsync(windows, singleOutputs, nextBatchNumber,
                        Stopwatch.GetTimestamp(), cancellationToken).ConfigureAwait(false);
                    nextBatchNumber += windows.Count;
                    Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - singleStarted);
                    continue;
                }

                IReadOnlyList<FamilyCandidateSet> dense = BuildDenseInputs(windows);
                bool hasPending = pendingWindows is not null;
                List<FamilyObservationWindow>? relicWindows = pendingWindows;
                IReadOnlyList<FamilyCandidateSet>? relicInputs = pendingNeow;
                long iterationStarted = Stopwatch.GetTimestamp();
                try
                {
                    (IReadOnlyList<FamilyCandidateSet> NeowResults, IReadOnlyList<FamilyCandidateSet>? RelicResults,
                        double SubmitMs, double SyncMs) epoch = await _executionContext.ExecutePrimaryGpuAsync(
                        rd =>
                        {
                            var epochContext = new FamilyEpoch(rd);
                            try
                            {
                                // 開啟指令清單前完成所有上傳。
                                neowEpoch.PrepareEpoch(epochContext, windows, dense, cancellationToken);
                                if (hasPending)
                                    relic.PrepareEpoch(epochContext, relicWindows!, relicInputs!, cancellationToken);
                                // 共用一份指令清單，只 Submit/Sync 一次。
                                epochContext.BeginList();
                                neowEpoch.RecordEpochDispatches(epochContext);
                                if (hasPending) relic.RecordEpochDispatches(epochContext);
                                (double submitMs, double syncMs) = epochContext.SubmitAndSync("EpochBatch");
                                IReadOnlyList<FamilyCandidateSet> neowResults =
                                    neowEpoch.CompleteEpoch(_executionContext, submitMs, syncMs);
                                IReadOnlyList<FamilyCandidateSet>? relicResults = hasPending
                                    ? relic.CompleteEpoch(_executionContext, 0d, 0d)
                                    : null;
                                return (neowResults, relicResults, submitMs, syncMs);
                            }
                            catch
                            {
                                epochContext.TryAbort();
                                neowEpoch.AbortEpoch();
                                relic.AbortEpoch();
                                throw;
                            }
                        }, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    Interlocked.Increment(ref _batchMergeGroups);
                    Interlocked.Add(ref _batchMergeWindows, windows.Count);
                    if (epoch.RelicResults is not null && pendingWindows is not null)
                        await EnqueueWithRemainingAsync(pendingWindows, epoch.RelicResults, pendingStartBatch,
                            remainingFamilies, cancellationToken).ConfigureAwait(false);
                    pendingWindows = windows;
                    pendingNeow = epoch.NeowResults;
                    pendingStartBatch = nextBatchNumber;
                    nextBatchNumber += windows.Count;
                }
                catch (FamilyBatchNotSupportedException)
                {
                    // 不支援的輸入會在提交前拒絕；按原批號、順序逐窗口重跑。
                    RuntimeLog.TryBackgroundWarning(
                        "nEpochGroupFallback=true;shape=unsupported;fallback=perWindow");
                    if (pendingWindows is not null && pendingNeow is not null)
                    {
                        IReadOnlyList<FamilyCandidateSet> pendingOutputs =
                            await LinearFamilyPipeline.FilterRemainingGroupAsync(
                                _executionContext, pendingWindows,
                                PrependRelic(relicFamily, remainingFamilies), pendingNeow, cancellationToken)
                                .ConfigureAwait(false);
                        CheckPipelineLookahead(pendingStartBatch, pendingWindows.Count);
                        await EnqueuePipelinedGroupAsync(pendingWindows, pendingOutputs, pendingStartBatch,
                            Stopwatch.GetTimestamp(), cancellationToken).ConfigureAwait(false);
                        // 待處理組已佔用批號，不能再加一次。
                    }
                    IReadOnlyList<FamilyCandidateSet> fallbackOutputs =
                        await LinearFamilyPipeline.FilterGroupAsync(
                            _executionContext, windows, _families, cancellationToken).ConfigureAwait(false);
                    await EnqueuePipelinedGroupAsync(windows, fallbackOutputs, nextBatchNumber,
                        Stopwatch.GetTimestamp(), cancellationToken).ConfigureAwait(false);
                    nextBatchNumber += windows.Count;
                    pendingWindows = null;
                    pendingNeow = null;
                }
                Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - iterationStarted);
            }

            // 最後一組仍須跑完 Relic 與下游。
            if (pendingWindows is not null && pendingNeow is not null &&
                Volatile.Read(ref _targetReached) == 0)
            {
                IReadOnlyList<FamilyCandidateSet> tailOutputs =
                    await FlushRelicEpochAsync(relic, pendingWindows, pendingNeow, cancellationToken)
                        .ConfigureAwait(false);
                await EnqueueWithRemainingAsync(pendingWindows, tailOutputs, pendingStartBatch,
                    remainingFamilies, cancellationToken).ConfigureAwait(false);
                pendingWindows = null;
                pendingNeow = null;
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

    private static IReadOnlyList<IFamilyInvocation> PrependRelic(
        IFamilyInvocation relicFamily, IReadOnlyList<IFamilyInvocation> remainingFamilies)
    {
        var chain = new IFamilyInvocation[remainingFamilies.Count + 1];
        chain[0] = relicFamily;
        for (int index = 0; index < remainingFamilies.Count; index++)
            chain[index + 1] = remainingFamilies[index];
        return chain;
    }

    /// <summary>補跑待處理組或尾組的 Relic。</summary>
    private async Task<IReadOnlyList<FamilyCandidateSet>> FlushRelicEpochAsync(
        IFamilyEpochInvocation relic, List<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await FamilyEpochFallback.RunAsync(
            () => _executionContext.ExecutePrimaryGpuAsync(rd =>
            {
                var epochContext = new FamilyEpoch(rd);
                try
                {
                    relic.PrepareEpoch(epochContext, windows, inputs, cancellationToken);
                    epochContext.BeginList();
                    relic.RecordEpochDispatches(epochContext);
                    (double submitMs, double syncMs) = epochContext.SubmitAndSync("EpochBatchTail");
                    return relic.CompleteEpoch(_executionContext, submitMs, syncMs);
                }
                catch
                {
                    epochContext.TryAbort();
                    relic.AbortEpoch();
                    throw;
                }
            }, cancellationToken).AsTask(),
            () =>
            {
                RuntimeLog.TryBackgroundWarning(
                    "nEpochTailFallback=true;shape=unsupported;fallback=perWindowRelic");
                // 只補跑 Relic；後續 family 與入佇列仍由呼叫端執行一次。
                return LinearFamilyPipeline.FilterRemainingGroupAsync(
                    _executionContext, windows, [(IFamilyInvocation)relic], inputs, cancellationToken).AsTask();
            }).ConfigureAwait(false);
    }

    private async Task EnqueueWithRemainingAsync(List<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> relicResults, int firstBatchNumber,
        IReadOnlyList<IFamilyInvocation> remainingFamilies, CancellationToken cancellationToken)
    {
        CheckPipelineLookahead(firstBatchNumber, windows.Count);
        IReadOnlyList<FamilyCandidateSet> finalResults =
            await LinearFamilyPipeline.FilterRemainingGroupAsync(
                _executionContext, windows, remainingFamilies, relicResults, cancellationToken)
                .ConfigureAwait(false);
        await EnqueuePipelinedGroupAsync(windows, finalResults, firstBatchNumber,
            Stopwatch.GetTimestamp(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ProduceFamilyBatchesCoreAsync(ulong startOrdinal, CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        int groupSize = FamilyBatchGrouping.GroupSize;
        int pipelineLookaheadLimit = EffectivePipelineLookaheadLimit;
        var windows = new List<FamilyObservationWindow>(groupSize);
        try
        {
            long remaining = _plan.ScanCount;
            ulong batchBase = startOrdinal;
            int batchNumber = 0;
            while (remaining > 0 && Volatile.Read(ref _targetReached) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 只合併獨立根窗口，保留各批的起點與數量。
                windows.Clear();
                ulong groupBase = batchBase;
                long groupRemaining = remaining;
                while (windows.Count < groupSize && groupRemaining > 0 &&
                       Volatile.Read(ref _targetReached) == 0)
                {
                    int groupedCount = (int)Math.Min(groupRemaining, _executionWindowSize);
                    windows.Add(new FamilyObservationWindow(new SearchBatch(groupBase, groupedCount), _plan));
                    groupBase = checked(groupBase + (ulong)groupedCount);
                    groupRemaining -= groupedCount;
                }
                if (windows.Count == 0) break;

                int nextBatchNumber = checked(batchNumber + 1);
                int overlappingExactBatch = Volatile.Read(ref _activeExactBatchNumber);
                if (overlappingExactBatch != 0)
                {
                    Interlocked.Exchange(ref _overlapObserved, 1);
                    Interlocked.Increment(ref _familyExactOverlapCount);
                }
                int pipelineLookahead = checked(
                    nextBatchNumber + windows.Count - 1 - (int)Interlocked.Read(ref _exactBatchesStarted));
                UpdateMaximum(ref _maxPipelineLookahead, pipelineLookahead);
                if (pipelineLookahead > pipelineLookaheadLimit)
                    RecordBoundedQueueViolation("PipelineLookaheadExceeded", pipelineLookahead);
                if (windows.Count > 1)
                {
                    Interlocked.Increment(ref _batchMergeGroups);
                    Interlocked.Add(ref _batchMergeWindows, windows.Count);
                }

                // Window sizing is coordinator/physical policy. SearchBatch itself
                // remains only BatchBase + BatchCandidateCount and owns no 2^24 rule.
                long familyPipelineStarted = Stopwatch.GetTimestamp();
                IReadOnlyList<FamilyCandidateSet> survivorsGroup;
                try
                {
                    survivorsGroup = await LinearFamilyPipeline.FilterGroupAsync(
                        _executionContext, windows, _families, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - familyPipelineStarted);
                }

                long familyCompletedTimestamp = Stopwatch.GetTimestamp();
                // 記帳、移交、游標與 Exact 仍逐批處理。
                for (int index = 0; index < windows.Count; index++)
                {
                    int currentBatchNumber = checked(nextBatchNumber + index);
                    FamilyCandidateSet survivors = survivorsGroup[index];
                    SearchBatch batch = windows[index].Batch;
                    int count = batch.BatchCandidateCount;
                    if (currentBatchNumber == 1) RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FirstRootBatchCompleted;ownerThreadId={_executionContext.GpuOwnerThreadId};sinceSessionMs={_clock.Elapsed.TotalMilliseconds:F4};roots={count};survivors={survivors.Count};plan={_executionPlan.PlanId}");
                    // 顯示用速率排除 GPU 初始化與首批，後續提交、移交及背壓均計入。
                    if (currentBatchNumber == 1)
                    {
                        bool cpuOnly = _families.Count > 0 && !_families.Any(f => f.ConditionPerformance.UsesGpu);
                        Interlocked.Exchange(ref _scanFirstCompletedTimestamp, cpuOnly ? familyPipelineStarted : familyCompletedTimestamp);
                        if (cpuOnly) Interlocked.Add(ref _scanAfterFirstRoots, count);
                    }
                    else Interlocked.Add(ref _scanAfterFirstRoots, count);
                    Interlocked.Exchange(ref _scanLastCompletedTimestamp, familyCompletedTimestamp);
                    var terminalBatch = new FamilyTerminalBatch(currentBatchNumber, survivors, familyCompletedTimestamp);
                    // 已移交 Exact 的批次不再算入 Channel。
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
                    batchNumber = currentBatchNumber;
                }
                remaining = groupRemaining;
                batchBase = groupBase;
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
        if (_plan.RunOptions.SkipExactValidation)
        {
            if (!TrustedRootHashInput.TryBindCanonicalSeed(_profile, rootHash, seed, out var unverifiedInput, out var issue))
                throw new InvalidOperationException("CandidateSeedBindingRejected:" + issue);
            if (!SeedPredictionRequest.TryCreateFromRootHash(unverifiedInput,
                    RolltheSpire2.Core.Identity.CharacterIdentity.FromKey(_plan.CharacterKey), _plan.Ascension,
                    _plan.Authority.PlayersCount, _plan.Authority.PlayerSlotIndex, _plan.Authority,
                    _plan.Evaluation.AncientOptionConditions, SeedPredictionDomainSelection.None,
                    SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, false, out var request, out var error))
                throw new InvalidOperationException("CandidateContextRejected:" + error);
            Interlocked.Increment(ref _scanned);
            Volatile.Write(ref _lastDisposition, (int)SearchDisposition.NotEvaluatedByPolicy);
            RecordOutcome(seed, SearchDisposition.NotEvaluatedByPolicy, string.Empty);
            await PublishCandidateAsync(new SearchCandidate(seed, _plan.ProfileId, _plan.CharacterKey, _plan.Ascension,
                _plan.SnapshotFingerprint, request!, null, _plan.Authority, [], [], []) { IsUnverified = true }, cancellationToken).ConfigureAwait(false);
            return;
        }
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
        if (!exact.IsMatch)
        {
            // Bounded evidence is emitted immediately, so an ongoing search/feedback export retains it.
            long rejected = Interlocked.Increment(ref _rejectionSampleCount);
            if (rejected <= 6) ExactRejectionDiagnostics.Log(_plan, seed, exact);
            if ((rejected & (rejected - 1)) == 0)
                RuntimeLog.TryBackgroundInfo("searchExactDiagnostics=true;phase=Progress;summaryJson=" + RuntimeLog.SafeJson(GetDiagnosticSummary()));
            return;
        }

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
        await PublishCandidateAsync(output, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_traceSingleCandidateSeed) &&
            string.Equals(seed, _traceSingleCandidateSeed, StringComparison.OrdinalIgnoreCase) &&
            _diagnosticSink is not null)
        {
            RuntimePredictionDiagnosticReporter.EmitAnalysis(
                _diagnosticSink, exact.Request!, exact.Document!, _plan.Detection.DisplayVersion,
                forceTrace: true, source: "FamilyExecutionExactTrace");
        }

    }

    private async ValueTask PublishCandidateAsync(SearchCandidate output, CancellationToken cancellationToken)
    {
        int matchNumber = TryReserveMatch();
        if (matchNumber == 0) return;
        if (_beforeCandidatePublish is not null)
            await _beforeCandidatePublish(matchNumber, cancellationToken).ConfigureAwait(false);
        await _candidates.Writer.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        // Count only successfully published outputs, including in diagnostic candidate mode.
        int published = Interlocked.Increment(ref _matches);
        Interlocked.CompareExchange(ref _firstResult, output, null); Volatile.Write(ref _lastResult, output);
        if (published >= _plan.TargetMatchCount)
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
            int current = Volatile.Read(ref _reservedMatches);
            if (current >= _plan.TargetMatchCount) return 0;
            if (Interlocked.CompareExchange(ref _reservedMatches, current + 1, current) == current) return current + 1;
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
            $"liveBatchStateLimit={EffectivePipelineLookaheadLimit}");
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
