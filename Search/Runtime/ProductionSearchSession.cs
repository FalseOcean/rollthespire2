using RolltheSpire2.Search.FamilyExecution;
using System.Diagnostics;
using System.Threading.Channels;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;

namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Complete Search lifecycle: root ranges, Filter allocation/batches, Predictor-backed
/// Exact validation, cancellation, ordered cursor commits and result publication.
/// </summary>
public sealed partial class ProductionSearchSession : IProductionSearchSession
{
    internal const int ExactOnlyMaximumBatchSize = 8192;
    // Initial engineering allowance for one compact pending-record payload.
    // Stops admission, never retention; in-flight failures may exceed it.
    internal const long DefaultPendingPayloadLimitBytes = 8L * 1024 * 1024;
    private readonly long _pendingPayloadLimitBytes;
    private Exception? _recoveryCapacityFailure;
    private readonly Action? _recoveryReceiptWaitStarted;
    private const int TerminalHandoffCapacity = 1;
    private const int TerminalOutstandingLimit = TerminalHandoffCapacity + 1;
    private const int LiveBatchStateLimit = TerminalHandoffCapacity + 2;
    private readonly PreparedSearch _plan;
    private readonly SearchModel _model;
    private readonly FamilyExecutionPlan _executionPlan;
    private FamilyExecutionPlan? _activeGpuPlan;
    private readonly AdaptiveGpuRootWindow? _adaptiveWindow;
    internal FamilyExecutionPlan ExecutionPlan => Volatile.Read(ref _gpuCreationCpuRecovery)?.Plan ?? Volatile.Read(ref _activeGpuPlan) ?? _executionPlan;
    private readonly FamilySearchEtaProjectionV1 _etaProjection;
    private readonly bool _logWorkload;
    private readonly IRuntimeProfile _profile;
    private IReadOnlyList<IFilterExecution> _families;
    private readonly SmallCandidateExactPolicy? _smallCandidateExactPolicy;
    private readonly List<FilterBodyDescription> _skippedCpuTail = [];
    private long _smallTailCpuRejected;
    private int _rootBatchPairGroups;
    private long _rootBatchPairAdmissionWaitTicks;
    private readonly Channel<SearchCandidate> _candidates;
    private readonly Channel<FamilyTerminalBatch> _terminalHandoff;
    private readonly FilterRuntimeServices _executionContext;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _executionWindowSize;
    private int _activeWindowSize;
    private readonly bool _usesGpu;
    private readonly SearchRecoveryLedger _recovery;
    private readonly Task _completion;
    private long _scanned;
    private int _reservedMatches;
    private int _matches;
    private int _targetCandidates;
    private int _confirmedCandidates;
    private int _unverifiedCandidates;
    private long _modeledNonHits;
    private int _modeledNonHitSampleCount;
    private string? _replayAssumptions;
    private int _state = (int)SearchRunState.Running;
    private int _lastDisposition = (int)SearchDisposition.NoMatch;
    private int _userCancellationRequested;
    private int _targetReached;
    private int _activeExactBatchNumber;
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
    private long _completedExactAttempts;
    private long _exactAggregateStopwatchTicks;
    private int _maxTerminalOutstanding;
    private int _maxPipelineLookahead;
    private int _orderedConsumptionViolationCount;
    private int _cursorContinuityViolationCount;
    private int _boundedQueueViolationCount;
    private string _failureCode = string.Empty;
    private readonly Func<SearchModel, TrustedRootHashInput, ProductionExactSearchResult>? _exactEvaluator;
    private readonly Action<CancellationToken>? _beforeExactEvaluate;
    private readonly Func<int, CancellationToken, ValueTask>? _beforeCandidatePublish;

    internal ProductionSearchSession(
        PreparedSearch plan,
        FamilyExecutionPlan executionPlan,
        FamilySearchEtaProjectionV1 etaProjection,
        int executionWindowSize,
        int queueCapacity,
        IRuntimePredictionDiagnosticSink? diagnosticSink,
        string traceSingleCandidateSeed,
        bool failOnPhysicalRecovery = false,
        Func<SearchModel, TrustedRootHashInput, ProductionExactSearchResult>? exactEvaluator = null,
        Func<int, CancellationToken, ValueTask>? beforeCandidatePublish = null,
        SearchContinuationSnapshot? continuation = null,
        Func<FilterRuntimeServices>? executionContextFactory = null,
        Func<Func<Task>, Task>? startRun = null,
        long pendingPayloadLimitBytes = DefaultPendingPayloadLimitBytes,
        Func<SearchPendingCandidate, long>? measurePendingPayload = null,
        Action? recoveryReceiptWaitStarted = null,
        bool allowInitialGpuCreationFallback = false,
        Action<CancellationToken>? beforeExactEvaluate = null,
        bool enableGpuPowerDiagnostics = false,
        bool logWorkload = false,
        bool enableAdaptiveGpuBatch = false)
    {
        if (executionWindowSize <= 0) throw new ArgumentOutOfRangeException(nameof(executionWindowSize));
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        if (pendingPayloadLimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(pendingPayloadLimitBytes));
        _pendingPayloadLimitBytes = pendingPayloadLimitBytes;
        _recoveryReceiptWaitStarted = recoveryReceiptWaitStarted;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _model = plan.Model;
        _exactEvaluator = exactEvaluator;
        _beforeExactEvaluate = beforeExactEvaluate;
        _beforeCandidatePublish = beforeCandidatePublish;
        _executionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        var cpuTailBodies = executionPlan.ExecutionOrderUnits
            .Where(body => body.SmallTailCpuMatcher is not null && body.RequiredPassedCoverage is null &&
                body.PlayersCount == 1 && _model.Authority.PlayersCount == 1).ToArray();
        void CaptureSkippedTail(IReadOnlyList<string> skipped)
        {
            foreach (var body in cpuTailBodies)
                if (skipped.Contains(body.FamilyId) && !_skippedCpuTail.Contains(body))
                    _skippedCpuTail.Add(body);
        }
        _smallCandidateExactPolicy = executionPlan.AllowSmallCandidateExact && !plan.RunOptions.SkipExactValidation
            ? new SmallCandidateExactPolicy(cpuTailBodies.Length > 0 ? CaptureSkippedTail : null) : null;
        _initialGpuCreationFallbackAllowed = allowInitialGpuCreationFallback && !failOnPhysicalRecovery &&
            !plan.RunOptions.SkipExactValidation && executionPlan.RankingAuthority != FamilyPlannerRankingAuthority.UnpricedPhysicalTrial &&
            !executionPlan.DecisionEvidence.Contains("ExplicitPhysicalSelection=true", StringComparer.Ordinal);
        _etaProjection = etaProjection ?? throw new ArgumentNullException(nameof(etaProjection));
        _logWorkload = logWorkload;
        _profile = RuntimeProfileRegistry.Select(plan.Model.Detection);
        _recovery = new(plan, _profile, continuation, measurePendingPayload);
        _executionWindowSize = executionWindowSize;
        _activeWindowSize = executionWindowSize;
        if (enableAdaptiveGpuBatch && plan.RunOptions.GpuRootBatchSize == 0 &&
            executionWindowSize == PrivateOrdinalBuffer.Capacity && plan.ScanCount >= 16L * PrivateOrdinalBuffer.Capacity)
        {
            var largest = FamilyExecutionCoordinator.WithGpuRootBatchSize(executionPlan, GpuRootWindow.Maximum);
            if (largest.Stages.Count != 0 && !ReferenceEquals(largest, executionPlan) &&
                largest.Stages[0].Family.WindowCapacity > executionWindowSize)
                _adaptiveWindow = new(largest.Stages[0].Family.WindowCapacity);
        }
        _usesGpu = executionPlan.OrderedFamilies.Any(body => body.UsesGpu);
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
        // Acquire the GPU owner only after validation and channel construction.
        // Until RunAsync is handed off successfully, this constructor owns cleanup.
        _executionContext = executionContextFactory is null
            ? new FilterRuntimeServices(requiresGpu: executionPlan.OrderedFamilies.Any(body => body.UsesGpu),
                failOnPhysicalRecovery, cpuWorkerCount: plan.WorkerCount) { CorrelationId=plan.SnapshotFingerprint,SkipExactValidation=plan.RunOptions.SkipExactValidation }
            : executionContextFactory();
        var selectedExecutions = new List<IFilterExecution>();
        try
        {
            _executionContext.SmallCandidateExact = _smallCandidateExactPolicy;
            _executionContext.BoundPublicGpuTail = executionPlan.Stages.Count > 1 && executionWindowSize > PrivateOrdinalBuffer.Capacity;
            if(executionPlan.OrderedFamilies.Any(body=>body.RequiresBonesCapsuleDiagnostics))
                _executionContext.BonesCapsuleDiagnostics=new(_model);
            var passedCoverage = new HashSet<string>(StringComparer.Ordinal);
            foreach (var body in executionPlan.OrderedFamilies)
            {
                _executionContext.BindCoverage(body,passedCoverage);
                selectedExecutions.Add((body.CreateExecution ?? throw new InvalidOperationException("SelectedFilterHasNoExecutionFactory:"+body.FamilyId))(body,_executionContext));
                passedCoverage.UnionWith(body.Coverage);
            }
            _families = selectedExecutions;
            _completion = startRun is null ? Task.Run(RunAsync) : startRun(RunAsync);
            if (enableGpuPowerDiagnostics && executionPlan.OrderedFamilies.Any(body => body.UsesGpu))
                TryDiagnostic(() => { _ = Task.Run(LogGpuPowerAsync); }, "GpuPowerDiagnostics.Start");
        }
        catch (Exception failure)
        {
            try
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(failure,
                    () =>
                    {
                        var failures=new List<Exception>();
                        foreach(var execution in selectedExecutions)
                            try{execution.DisposeAsync(_executionContext).AsTask().GetAwaiter().GetResult();}catch(Exception error){failures.Add(error);}
                        try{_executionContext.DisposeAsync().AsTask().GetAwaiter().GetResult();}catch(Exception error){failures.Add(error);}
                        if(failures.Count>0)throw new AggregateException("FilterBindingCleanup",failures);
                    }, "ProductionSearchSession.Construction");
            }
            finally { _lifetime.Dispose(); }
            throw;
        }
    }

    internal FamilyPerformanceObservation CaptureFilterObservation(FilterBodyDescription body)
    {
        var execution = FindBoundExecution(body);
        var resource=execution?.ResourceInfo ?? (Device:"",SetupMs:0d);
        return _executionContext.Costs.CaptureLatest(execution?.Body ?? body,resource.Device,resource.SetupMs);
    }
    // Null means the descriptor has no directly bound public handle. Private
    // children report completed samples through the observer, not invented state.
    internal bool? IsFilterExecutionFaulted(FilterBodyDescription body) =>
        FindBoundExecution(body)?.ExecutionFaulted;

    private IFilterExecution? FindBoundExecution(FilterBodyDescription body)
    {
        var bound = Volatile.Read(ref _families);
        for (int i = 0; i < bound.Count; i++)
            if (ReferenceEquals(bound[i].Body, body) || (bound.Count == _executionPlan.Stages.Count &&
                ReferenceEquals(_executionPlan.Stages[i].Family, body)))
                return bound[i];
        return null;
    }

    public Task Completion => _completion;
    public IReadOnlyList<string> FamilyIds => ExecutionPlan.OrderedFamilies.Select(family => family.FamilyId).ToArray();
    // Same-run timing observations never reprice the selected session evidence.
    internal FamilySearchEtaProjectionV1 EtaProjection => Volatile.Read(ref _gpuCreationCpuRecovery)?.Eta ?? _etaProjection;
    private const int PlayingWindowLimit = 65_536;
    private const int PlayingBatchDelayMilliseconds = 20;
    private volatile bool _usedPlayingBudget;
    internal bool PlayingBudget
    {
        get => _executionContext.PlayingBudget;
        set
        {
            if (value) _usedPlayingBudget = true;
            _executionContext.PlayingBudget = value;
        }
    }
    internal int ExecutionWindowSize => Volatile.Read(ref _gpuCreationCpuRecovery)?.WindowSize ?? Volatile.Read(ref _activeWindowSize);

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
            (SearchRunState)Volatile.Read(ref _state), scanned, Volatile.Read(ref _confirmedCandidates),
            _plan.ScanCount, _plan.TargetMatchCount, elapsed, elapsed <= 0d ? 0d : scanned / elapsed,
            (SearchDisposition)Volatile.Read(ref _lastDisposition), Volatile.Read(ref _failureCode) ?? string.Empty,
            Volatile.Read(ref _userCancellationRequested) != 0)
        {
            UnverifiedCandidateCount = Volatile.Read(ref _unverifiedCandidates),
            ModeledNonHitCount = Interlocked.Read(ref _modeledNonHits),
            ModeledNonHitSampleCount = Volatile.Read(ref _modeledNonHitSampleCount),
            ModeledNonHitSampleReceived = _recovery.ModeledNonHitSampleReceived,
            ReplayAssumptions = Volatile.Read(ref _replayAssumptions) ?? string.Empty,
            PendingCandidateCount = _recovery.PendingCount,
            CleanupIssue = Volatile.Read(ref _cleanupIssue),
            ExecutionWarnings = CaptureExecutionWarnings(),
            ActiveGpuRootBatchSize = _usesGpu && Volatile.Read(ref _gpuCreationCpuRecovery) is null
                ? (PlayingBudget ? Math.Min(ExecutionWindowSize, PlayingWindowLimit) : ExecutionWindowSize) : null,
            ObservedScanningSeconds = ElapsedMilliseconds(Math.Max(0,Interlocked.Read(ref _scanLastCompletedTimestamp)-Interlocked.Read(ref _scanFirstCompletedTimestamp)))/1000,
            ObservedScanningRoots = Interlocked.Read(ref _scanAfterFirstRoots)
        };
    }

    public ulong GetSafeNextOrdinal() => _recovery.SafeNextOrdinal;

    private FamilyExecutionPlan PlanForWindow(int window) =>
        window == PrivateOrdinalBuffer.Capacity ? _executionPlan :
            FamilyExecutionCoordinator.WithGpuRootBatchSize(_executionPlan, window);

    private async ValueTask RebindGpuOrderAsync(FamilyExecutionPlan target, int window, GpuRootWindowBindingRefused? refusal = null)
    {
        // A suffix binding can be refused while the dense producer still owns
        // a wide buffer. Release every old slot before retrying this frontier.
        var previous = _families;
        IFilterExecution?[] ordinary = previous.ToArray();
        var failures = new List<Exception>();
        for (int i = previous.Count - 1; i >= 0; i--)
        {
            var execution = previous[i];
            if (!execution.Body.UsesGpu || _executionContext.TryGetCpuFilterIsolation(execution, out _)) continue;
            ordinary[i] = null;
            try { await execution.DisposeAsync(_executionContext).ConfigureAwait(false); }
            catch (Exception failure) { failures.Add(failure); }
        }
        Volatile.Write(ref _families, ordinary.OfType<IFilterExecution>().ToArray());
        if (failures.Count > 0)
        {
            if (refusal is not null) failures.Insert(0, refusal);
            throw new AggregateException("PublicGpuWindow.ReleaseFailed", failures);
        }
        if (_executionContext.GpuOwnerThreadId != 0)
            await _executionContext.ExecuteGpuAsync(rd =>
            {
                GpuRootStorageBudget.DrainRetiredWindowIfNeeded(rd, window);
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
        try
        {
            for (int i = 0; i < previous.Count; i++)
            {
                if (ordinary[i] is not null) continue;
                var body = target.Stages[i].Family;
                _executionContext.BindCoverage(body, _executionContext.CoverageFor(previous[i].Body));
                ordinary[i] = body.CreateExecution!(body, _executionContext);
            }
        }
        catch (Exception bindingFailure)
        {
            // The session owns the new, partially constructed execution list.
            // RunAsync drains it through the normal fault/cleanup path.
            Volatile.Write(ref _families, ordinary.OfType<IFilterExecution>().ToArray());
            if (refusal is null) throw;
            throw new AggregateException("PublicGpuWindow.RebindFailed", refusal, bindingFailure);
        }
        Volatile.Write(ref _families, ordinary!);
        _executionContext.BoundPublicGpuTail = target.Stages.Count > 1 && window > PrivateOrdinalBuffer.Capacity;
        Volatile.Write(ref _activeGpuPlan, target);
        Volatile.Write(ref _activeWindowSize, window);
    }

    public bool TryGetSafeNextOrdinal(out ulong nextOrdinal) => _recovery.TryGetSafeNextOrdinal(out nextOrdinal);

    public async ValueTask DisposeAsync()
    {
        Cancel();
        try { await _completion.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        bool workloadLogged = false;
        try
        {
            if (!VisibleSeedCandidateCodec.TryParseOrdinal(_profile, _plan.CanonicalStartSeed, out ulong startOrdinal, out _, out string issue))
                throw new InvalidOperationException("StartSeedOrdinalRejected:" + issue);

            SetSafeCursor(startOrdinal);
            // Only an explicit new session retries retained failures, once each.
            // Retry does not advance the ordinary scan frontier.
            await RetryPendingCandidatesAsync(_lifetime.Token).ConfigureAwait(false);
            await RecoverUnreceivedCandidatesAsync(_lifetime.Token).ConfigureAwait(false);
            Task exactConsumer = ConsumeExactBatchesAsync(_lifetime.Token);
            Task familyProducer = ProduceFamilyBatchesAsync(startOrdinal, _lifetime.Token);
            // Once the workers have started, record the frozen request and
            // selected plan while the first batch is in flight.
            long evidenceStarted = Stopwatch.GetTimestamp();
            if (_logWorkload)
            {
                workloadLogged = true;
                LogWorkloadEvidence();
            }
            TryDiagnostic(LogStartEvidence, "SessionStartEvidence");
            RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=SessionStartEvidence;elapsedMs={Stopwatch.GetElapsedTime(evidenceStarted).TotalMilliseconds:F4}");
            await Task.WhenAll(familyProducer, exactConsumer).ConfigureAwait(false);
            if (Volatile.Read(ref _recoveryCapacityFailure) is not null)
                throw new OperationCanceledException(_lifetime.Token);

            Volatile.Write(ref _state,
                Volatile.Read(ref _userCancellationRequested) != 0
                    ? (int)SearchRunState.Cancelled
                    : (int)SearchRunState.Completed);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _recoveryCapacityFailure) is not null)
        {
            var failure = Volatile.Read(ref _recoveryCapacityFailure)!;
            Volatile.Write(ref _failureCode, failure.Message);
            Volatile.Write(ref _state, (int)SearchRunState.Faulted);
            LogFault(failure, "RecoveryCapacity");
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _targetReached) != 0)
        {
            Volatile.Write(ref _state, (int)SearchRunState.Completed);
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _userCancellationRequested) != 0)
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
            if (_logWorkload && !workloadLogged) LogWorkloadEvidence();
            _terminalHandoff.Writer.TryComplete();
            bool physicalRecoveryContaminated = Volatile.Read(ref _initialGpuCreationRecoveryAttempted) != 0;
            // Contended sessions cannot establish the cost of a foreground full-speed search.
            bool performanceEvidenceUnavailable = _usedPlayingBudget;
            foreach (IFilterExecution family in _families)
            {
                try { await family.DisposeAsync(_executionContext).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    RecordCleanupFailure(ex, "FamilyCleanup:" + family.Body.FamilyId);
                    _executionContext.Costs.Invalidate(family.Body);
                    physicalRecoveryContaminated = true;
                    RuntimeLog.TryBackgroundWarning(
                        $"familyResourceDisposeFailed=true;family={family.GetType().Name};error={ex.GetType().Name}:{ex.Message}");
                }
                try
                {
                    if (family.ExecutionFaulted) _executionContext.Costs.Invalidate(family.Body);
                    var resource=family.ResourceInfo;
                    FamilyPerformanceObservation observation = _executionContext.Costs.CaptureLatest(family.Body,resource.Device,resource.SetupMs);
                    if (string.Equals(observation.EligibilityEvidence, "PhysicalFailureOrRecovery", StringComparison.Ordinal))
                        physicalRecoveryContaminated = true;
                    RuntimeLog.TryBackgroundInfo(
                        $"familyPerformanceObservation=true;family={family.Body.FamilyId};revision={family.Body.ConditionPerformance.PhysicalImplementationRevision};gpu={family.Body.ConditionPerformance.UsesGpu};input={observation.InputCandidates};output={observation.OutputCandidates};canonicalMs={observation.PerformanceBoundaryMs};legacyPeakAuthority=false");
                }
                catch (Exception ex)
                {
                    performanceEvidenceUnavailable = true;
                    RecordDiagnosticFailure(ex, "PerformanceObservation:" + family.Body.FamilyId);
                }
            }
            long exactAttempts = Interlocked.Read(ref _exactAttempts);
            long completedExactAttempts = Interlocked.Read(ref _completedExactAttempts);
            double exactAggregateMs = ElapsedMilliseconds(Interlocked.Read(ref _exactAggregateStopwatchTicks));
            string exactTimingRevision = "";
            bool exactTimingSubmitted = false;
            // Empty queries return seeds but do not measure normal Exact work.
            if (FamilyExactTimingDomains.HasQueryWork(_model.CompiledSearch.NormalizedQuery) &&
                !physicalRecoveryContaminated && !performanceEvidenceUnavailable && completedExactAttempts > 0 && exactAggregateMs > 0d)
            {
                try
                {
                    exactTimingRevision = FamilyExactTimingDomains.ImplementationRevision(_model);
                    SearchPredictabilityVerificationStore.SubmitExactTimingSample(
                    FamilyExactTimingDomains.Resolve(_model),
                    completedExactAttempts,
                    exactAggregateMs,
                    $"FamilyExecutionExactTiming;state={(SearchRunState)Volatile.Read(ref _state)};" +
                    "acceptanceIgnored=true;runtimeSurvivalAuthority=false;implementationRevision=" + exactTimingRevision,
                    exactTimingRevision);
                    exactTimingSubmitted = true;
                }
                catch (Exception ex) { RecordDiagnosticFailure(ex, "ExactTimingSample"); }
            }
            RuntimeLog.TryBackgroundInfo(
                $"familyExactTimingObservation=true;submitted={exactTimingSubmitted.ToString().ToLowerInvariant()};" +
                $"exactAttempts={exactAttempts};smallTailCpuRejected={Interlocked.Read(ref _smallTailCpuRejected)};completedExactAttempts={completedExactAttempts};exactAggregateMs={F(exactAggregateMs)};" +
                $"implementationRevision={(exactTimingRevision.Length == 0 ? "Legacy" : exactTimingRevision)};" +
                $"physicalRecoveryContaminated={physicalRecoveryContaminated.ToString().ToLowerInvariant()};" +
                "timingOnly=true;acceptanceIgnored=true;survivalAuthority=false;diskIoOnWorker=false");
            try { await _executionContext.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                RecordCleanupFailure(ex, "OwnerCleanup");
                RuntimeLog.TryBackgroundWarning(
                    $"familyExecutionContextDisposeFailed=true;error={ex.GetType().Name}:{ex.Message}");
            }
            try { _executionContext.Costs.WriteSummary(); } catch(Exception ex) { RecordDiagnosticFailure(ex,"ExecutionEvidence"); }
            LogResultEvidence(exactAttempts);
            if (!physicalRecoveryContaminated && !performanceEvidenceUnavailable && (SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted)
            {
                try { GpuCostCalibration.Submit(_executionContext.Costs.GpuPeaks); }
                catch (Exception ex) { RecordDiagnosticFailure(ex, "GpuCalibrationSample"); }
                try { CpuCostCalibration.Submit(_executionContext.Costs.CpuPeaks); }
                catch (Exception ex) { RecordDiagnosticFailure(ex, "CpuCalibrationSample"); }
            }
            _candidates.Writer.TryComplete();
            LogCompletionEvidence();
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
            PreparedFamilyBatch? preparedNext = null;
            long groupStarted = 0;
            int groupDelivered = 0, measuredRoots = 0;
            while (remaining > 0 && Volatile.Read(ref _targetReached) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int nextBatchNumber = checked(batchNumber + 1);
                int overlappingExactBatch = Volatile.Read(ref _activeExactBatchNumber);
                if (overlappingExactBatch != 0)
                {
                    Interlocked.Increment(ref _familyExactOverlapCount);
                }
                int pipelineLookahead = checked(nextBatchNumber - (int)Interlocked.Read(ref _exactBatchesStarted));
                UpdateMaximum(ref _maxPipelineLookahead, pipelineLookahead);
                if (pipelineLookahead > LiveBatchStateLimit)
                    RecordBoundedQueueViolation("PipelineLookaheadExceeded", pipelineLookahead);

                // Window sizing is coordinator/physical policy. SearchBatch itself
                // remains only BatchBase + BatchCandidateCount and owns no 2^24 rule.
                bool playing = PlayingBudget;
                if (playing) await Task.Delay(PlayingBatchDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                int window = playing ? Math.Min(ExecutionWindowSize, PlayingWindowLimit) : ExecutionWindowSize;
                bool measureWindow = _adaptiveWindow is not null && window == ExecutionWindowSize &&
                    Volatile.Read(ref _gpuCreationCpuRecovery) is null;
                if (preparedNext is null && measureWindow)
                {
                    groupStarted = Stopwatch.GetTimestamp();
                    groupDelivered = Volatile.Read(ref _targetCandidates);
                    measuredRoots = 0;
                    int chosen = _adaptiveWindow!.Choose(remaining, Math.Max(0, _plan.TargetMatchCount - groupDelivered));
                    if (chosen != window)
                    {
                        await RebindGpuOrderAsync(PlanForWindow(chosen), chosen).ConfigureAwait(false);
                        RuntimeLog.TryBackgroundInfo($"adaptiveGpuWindow=true;state={_adaptiveWindow.State};selected={_adaptiveWindow.Selected};from={window};to={chosen};frontier={batchBase};remaining={remaining}");
                        window = chosen;
                    }
                }
                if (!measureWindow) groupStarted = 0;
                // A pair submitted before a mode change owns its original ordinal range.
                int count = preparedNext?.Batch.BatchCandidateCount ?? (int)Math.Min(remaining, window);
                var batch = new SearchBatch(batchBase, count);
                var observation = batch;
                long familyPipelineStarted = Stopwatch.GetTimestamp();
                long pairAdmissionWait = 0;
                _skippedCpuTail.Clear();
                FamilyCandidateSet survivors;
                try
                {
                    PreparedFamilyBatch? prepared = preparedNext;
                    preparedNext = null;
                    if (!playing && window <= PrivateOrdinalBuffer.Capacity && FamilyDenseBatchGroupingPolicy.Enabled && prepared is null && batchNumber > 0 && remaining > count + SmallCandidateExactPolicy.MaximumCandidates &&
                        Volatile.Read(ref _gpuCreationCpuRecovery) is null && _families.Count > 0 &&
                        _families[0] is IDenseFamilyBatchGrouping { CanGroupDenseBatches: true, PreferDenseBatchGrouping: true } grouping)
                    {
                        // Single writer: once the terminal queue can accept a
                        // member, two physical prefixes plus the active Exact
                        // batch fit the unchanged three-live-batch bound.
                        long readyStarted = Stopwatch.GetTimestamp();
                        bool ready = await _terminalHandoff.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
                        pairAdmissionWait = Stopwatch.GetTimestamp() - readyStarted;
                        Interlocked.Add(ref _rootBatchPairAdmissionWaitTicks, pairAdmissionWait);
                        if (!ready) throw new InvalidOperationException("RootPair.TerminalChannelClosed");
                        if (Volatile.Read(ref _targetReached) != 0) break;
                        int groupedLookahead = checked(nextBatchNumber + 1 - (int)Interlocked.Read(ref _exactBatchesStarted));
                        if (groupedLookahead <= LiveBatchStateLimit)
                        {
                            int secondCount = (int)Math.Min(remaining - count, ExecutionWindowSize);
                            var secondBatch = new SearchBatch(checked(batchBase + (ulong)count), secondCount);
                            var group = await grouping.PrepareDensePairAsync(_executionContext, observation,
                                secondBatch, cancellationToken).ConfigureAwait(false);
                            if (group.First.Batch != batch || group.Second.Batch != secondBatch)
                                throw new InvalidDataException("RootPair.PreparedBatchIdentityMismatch");
                            prepared = group.First;
                            preparedNext = group.Second;
                            Interlocked.Increment(ref _rootBatchPairGroups);
                            if (_rootBatchPairGroups == 1)
                                RuntimeLog.TryBackgroundInfo($"rootBatchPairBound=true;physicalRevision={NeowFamilyGpuExecutor.DensePairRevision};rootWindows=2;sharedWaitChargedOnce=true;localCalibration=false");
                            UpdateMaximum(ref _maxPipelineLookahead, groupedLookahead);
                        }
                    }
                    survivors = Volatile.Read(ref _gpuCreationCpuRecovery) is not null
                        ? FamilyCandidateSet.Dense(batch)
                        : await LinearFamilyPipeline.FilterAsync(
                            _executionContext, observation, _families, _smallCandidateExactPolicy,
                            cancellationToken, prepared).ConfigureAwait(false);
                }
                catch (GpuRootWindowBindingRefused refusal) when (!cancellationToken.IsCancellationRequested &&
                    !_executionContext.FailOnPhysicalRecovery && window > PrivateOrdinalBuffer.Capacity)
                {
                    // The binding owner released the oversized resources. No results
                    // from this batch reached the terminal queue or advanced the cursor.
                    _executionContext.ReleaseFilterIsolations(batch);
                    _adaptiveWindow?.Refuse();
                    int retryWindow = _adaptiveWindow?.Current ?? PrivateOrdinalBuffer.Capacity;
                    if (_adaptiveWindow is not null || _executionContext.BoundPublicGpuTail)
                        await RebindGpuOrderAsync(PlanForWindow(retryWindow), retryWindow, refusal).ConfigureAwait(false);
                    else
                        Volatile.Write(ref _activeWindowSize, retryWindow);
                    TryDiagnostic(() => RuntimeLog.TryBackgroundWarning($"gpuRootWindowReduced=true;batchBase={batch.BatchBase};requested={_plan.RunOptions.GpuRootBatchSize};from={window};to={retryWindow};context={_plan.SnapshotFingerprint};exceptionJson={RuntimeLog.SafeJson(refusal.ToString())}"), "GpuRootWindowReduced");
                    continue;
                }
                catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
                {
                    if (!await TryRecoverInitialGpuCreationAsync(failure, batch, cancellationToken).ConfigureAwait(false)) throw;
                    // No terminal batch escaped. Recreate this exact frontier
                    // with the smaller CPU window, without advancing any cursor.
                    continue;
                }
                finally
                {
                    Interlocked.Add(ref _familyPipelineWallTicks, Stopwatch.GetTimestamp() - familyPipelineStarted - pairAdmissionWait);
                }
                foreach (var warning in BatchExecutionWarnings(batch)) _executionWarnings.TryAdd(warning, 0);

                long familyCompletedTimestamp = Stopwatch.GetTimestamp();
                if(nextBatchNumber==1) RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=FirstRootBatchCompleted;ownerThreadId={_executionContext.GpuOwnerThreadId};sinceSessionMs={_clock.Elapsed.TotalMilliseconds:F4};roots={count};survivors={survivors.Count};plan={ExecutionPlan.PlanId}");
                // Presentation-only interval between completed root batches: excludes lazy GPU setup
                // and the GPU first batch. CPU may include its first completed pure scanning batch.
                // Includes subsequent submit/handoff/backpressure elapsed time.
                if(nextBatchNumber==1)
                {
                    bool cpuOnly=_families.Count>0 && !_families.Any(f=>f.Body.UsesGpu);
                    Interlocked.Exchange(ref _scanFirstCompletedTimestamp,cpuOnly?familyPipelineStarted:familyCompletedTimestamp);
                    if(cpuOnly)Interlocked.Add(ref _scanAfterFirstRoots,count);
                }
                else Interlocked.Add(ref _scanAfterFirstRoots,count);
                Interlocked.Exchange(ref _scanLastCompletedTimestamp,familyCompletedTimestamp);
                // Only the serial producer collects skipped bodies. Snapshot for this batch
                // before the next root window can overlap its consumer.
                var terminalBatch = new FamilyTerminalBatch(nextBatchNumber, survivors, familyCompletedTimestamp,
                    _skippedCpuTail.ToArray());
                // Channel ownership excludes a batch already transferred to Exact.
                int terminalOutstanding = _terminalHandoff.Reader.Count + 1;
                UpdateMaximum(ref _maxTerminalOutstanding, terminalOutstanding);
                if (terminalOutstanding > TerminalOutstandingLimit)
                    RecordBoundedQueueViolation("TerminalOutstandingExceeded", terminalOutstanding);
                terminalBatch.MarkTerminalOffered();
                await _terminalHandoff.Writer.WriteAsync(terminalBatch, cancellationToken).ConfigureAwait(false);
                if (groupStarted != 0 && measureWindow)
                {
                    measuredRoots += count;
                    if (preparedNext is null && count == window && measuredRoots % window == 0)
                    {
                        // Paired GPU submissions share the same wait. Count both
                        // only after their ordered handoffs have absorbed backpressure.
                        int windows = measuredRoots / window;
                        double ms = Stopwatch.GetElapsedTime(groupStarted).TotalMilliseconds / windows;
                        int delivered = Volatile.Read(ref _targetCandidates) - groupDelivered;
                        for (int member = 0; member < windows; member++)
                            _adaptiveWindow!.Observe(window, ms, member == windows - 1 ? delivered : 0);
                    }
                }
                Interlocked.Add(
                    ref _familyToTerminalEnqueueTicks,
                    Stopwatch.GetTimestamp() - terminalBatch.FamilyCompletedTimestamp);
                Interlocked.Increment(ref _familyBatchesProduced);
                batchNumber = nextBatchNumber;
                remaining -= count;
                batchBase = checked(batchBase + (ulong)count);
            }
        }
        catch (DeferredRootBatchFailure failure)
        {
            // GPU work is already synchronized and only this later member is
            // untrusted. Completing with its original cause lets the ordered
            // consumer finish every already-offered batch before seeing it.
            completionError = failure.Original;
            RuntimeLog.TryBackgroundWarning($"rootGroupDeferredFault=true;batch={failure.Batch.BatchBase};drainTrustedTerminal=true;cause={RuntimeLog.SafeJson(failure.Original.ToString())}");
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
                        candidates, terminalBatch.CpuTail,
                        exactStartTimestamp,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _executionContext.ReleaseFilterIsolations(candidates.Batch);
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
        FamilyCandidateSet candidates, FilterBodyDescription[] cpuTail,
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

        // ABI1 is strictly ordered. A survivor is accounted for only by its
        // model outcome/publication or a retained pending failure. This array
        // tracks traversal, not a proof that every check succeeded; durable
        // continuation always includes the separate pending and delivery ledger.
        var completed = new bool[survivorCount];
        Interlocked.Add(ref _completionTrackingCount, survivorCount);
        ReadOnlyMemory<ulong> compactAbi1 = candidates.IsDense ? default : candidates.ExportAbi1();
        long exactWorkCompletedTimestamp = 0;
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, survivorCount),
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = PlayingBudget ? 1 : _plan.WorkerCount },
                async (survivorIndex, token) =>
                {
                    if (Volatile.Read(ref _targetReached) != 0) return;
                    ulong logicalOrdinal = candidates.IsDense
                        ? (ulong)survivorIndex
                        : compactAbi1.Span[survivorIndex];
                    if (await EvaluateCandidateAsync(candidates.Batch, logicalOrdinal, token, cpuTail: cpuTail).ConfigureAwait(false))
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

    private async ValueTask<bool> EvaluateCandidateAsync(SearchBatch batch, ulong logicalOrdinal, CancellationToken cancellationToken,
        bool retrying = false, FilterBodyDescription[]? cpuTail = null)
    {
        ulong globalCandidate = batch.GlobalCandidate(logicalOrdinal);
        if (!retrying && _recovery.WasAlreadyHandled(globalCandidate))
        {
            Interlocked.Increment(ref _scanned);
            return true;
        }
        if (!retrying && !_plan.RunOptions.SkipExactValidation &&
            !await AdmitNewCandidateAsync(cancellationToken).ConfigureAwait(false)) return false;
        string seed;
        TrustedRootHashInput input;
        await _executionContext.EnterCpuAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            seed = VisibleSeedCandidateCodec.FormatOrdinal(_profile, globalCandidate);
            ulong rootHash = _profile.ComputeRootSeed(seed);
            if (!TrustedRootHashInput.TryBindCanonicalSeed(_profile, rootHash, seed, out input, out var issue))
                throw new InvalidOperationException("CandidateSeedBindingRejected:" + issue);
        }
        finally { _executionContext.ExitCpu(); }
        if (_plan.RunOptions.SkipExactValidation)
        {
            if (!retrying) Interlocked.Increment(ref _scanned);
            Volatile.Write(ref _lastDisposition, (int)SearchDisposition.NotEvaluatedByPolicy);
            RecordOutcome(seed, SearchDisposition.NotEvaluatedByPolicy, string.Empty);
            return await PublishCandidateAsync(input, batch, globalCandidate, cancellationToken).ConfigureAwait(false);
        }
        SearchQueryEvaluation evaluation;
        ProductionExactSearchResult exact;
        await _executionContext.EnterCpuAsync(cancellationToken).ConfigureAwait(false);
        long exactAttemptStarted = 0;
        bool exactCompleted = false;
        try
        {
            if (cpuTail is not null)
            {
                foreach (var body in cpuTail)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool survives;
                    try { survives = body.SmallTailCpuMatcher!(input.RootHash); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { throw new InvalidOperationException("SmallCandidateCpuTail:" + body.FamilyId, error); }
                    if (survives) continue;
                    Interlocked.Increment(ref _smallTailCpuRejected);
                    Interlocked.Increment(ref _scanned);
                    return true;
                }
            }
            Interlocked.Increment(ref _exactAttempts);
            exactAttemptStarted = Stopwatch.GetTimestamp();
            _beforeExactEvaluate?.Invoke(cancellationToken);
            // Parallel.ForEachAsync owns a child token whose propagation may lag
            // session cancellation. Exact and its consumer must use the same token.
            exact = _exactEvaluator is null
                ? ProductionExactSearchEvaluator.EvaluateCandidate(_model, input, cancellationToken)
                : _exactEvaluator(_model, input);
            if (exact.Facts is { } facts) ProductionExactSearchEvaluator.ThrowIfPredictionFailed(facts);
            evaluation = exact.Evaluation;
            exactCompleted = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException cancelled ||
            !cancellationToken.IsCancellationRequested || cancelled.CancellationToken != cancellationToken)
        {
            RetainFailedCandidate(globalCandidate, seed, ex);
            if (!retrying) Interlocked.Increment(ref _scanned);
            // Accounted for by the continuation ledger, never completed as NoMatch.
            return true;
        }
        finally
        {
            // A failed/aborted check did not execute the full priced work. It
            // remains pending, but cannot teach a misleadingly cheap Exact rate.
            if (exactCompleted)
            {
                Interlocked.Increment(ref _completedExactAttempts);
                Interlocked.Add(ref _exactAggregateStopwatchTicks,
                    Math.Max(0L, Stopwatch.GetTimestamp() - exactAttemptStarted));
            }
            _executionContext.ExitCpu();
        }

        if (!retrying) Interlocked.Increment(ref _scanned);
        Volatile.Write(ref _lastDisposition, (int)evaluation.Disposition);
        RecordOutcome(seed, evaluation.Disposition, evaluation.FailureCode);
        if (!exact.IsMatch)
        {
            RecordRejectedCandidate(seed, exact);
            if (evaluation.ReplayEstimate is { } estimate)
            {
                Interlocked.CompareExchange(ref _replayAssumptions, estimate.Assumptions, null);
                if (estimate.Disposition == SearchDisposition.NoMatch)
                {
                    Interlocked.Increment(ref _modeledNonHits);
                    bool sampled = await PublishCandidateAsync(input, batch, globalCandidate,
                        cancellationToken, exact, modeledNonHitSample: true).ConfigureAwait(false);
                    _recovery.ResolveCandidate(globalCandidate, published: sampled);
                    return true;
                }
            }
            if (evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported)
            {
                bool published = await PublishCandidateAsync(input, batch, globalCandidate,
                    cancellationToken, exact).ConfigureAwait(false);
                if (published) _recovery.ResolveCandidate(globalCandidate, published: true);
                return published;
            }
            _recovery.ResolveCandidate(globalCandidate);
            return true;
        }

        if (!await PublishCandidateAsync(input, batch, globalCandidate, cancellationToken, exact).ConfigureAwait(false))
            return false;
        _recovery.ResolveCandidate(globalCandidate, published: true);

        TraceCandidate(seed, exact);
        return true;
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

    private void SetSafeCursor(ulong nextOrdinal)
    {
        if (_recovery.TryAdvanceCursor(nextOrdinal, out ulong currentOrdinal)) return;
        int violationCount = Interlocked.Increment(ref _cursorContinuityViolationCount);
        if (violationCount == 1)
            RuntimeLog.TryBackgroundWarning(
                $"familyCursorContinuityViolation=true;currentSafeNextOrdinal={currentOrdinal};" +
                $"attemptedSafeNextOrdinal={nextOrdinal}");
    }

    private void RecordBoundedQueueViolation(string issue, int observedDepth)
    {
        int violationCount = Interlocked.Increment(ref _boundedQueueViolationCount);
        if (violationCount != 1) return;
        RuntimeLog.TryBackgroundWarning(
            $"familyBoundedQueueViolation=true;issue={issue};observedDepth={observedDepth};" +
            $"terminalHandoffCapacity={TerminalHandoffCapacity};" +
            $"terminalOutstandingLimit={TerminalOutstandingLimit};terminalOutstandingDefinition=QueuedPlusCurrentOffer;" +
            $"liveBatchStateLimit={LiveBatchStateLimit};rootBatchPairGroups={_rootBatchPairGroups};rootBatchPairAdmissionWaitTicks={_rootBatchPairAdmissionWaitTicks};rootBatchPairSharedWaitChargedOnce=true");
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
            long familyCompletedTimestamp, FilterBodyDescription[] cpuTail)
        {
            BatchNumber = batchNumber;
            CpuTail = cpuTail;
            Candidates = candidates;
            FamilyCompletedTimestamp = familyCompletedTimestamp;
        }

        public int BatchNumber { get; }
        public FilterBodyDescription[] CpuTail { get; }
        public FamilyCandidateSet Candidates { get; }
        public long FamilyCompletedTimestamp { get; }
        public long TerminalOfferTimestamp { get; private set; }

        public void MarkTerminalOffered() => TerminalOfferTimestamp = Stopwatch.GetTimestamp();
    }
}
