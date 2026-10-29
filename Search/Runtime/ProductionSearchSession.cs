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
    private readonly ExactSearchExecutionRequest _plan;
    private readonly FamilyExecutionPlan _executionPlan;
    internal FamilyExecutionPlan ExecutionPlan => Volatile.Read(ref _gpuCreationCpuRecovery)?.Plan ?? _executionPlan;
    private readonly FamilySearchEtaProjectionV1 _etaProjection;
    private readonly IRuntimeProfile _profile;
    private readonly IReadOnlyList<IFamilyInvocation> _families;
    private readonly SmallCandidateExactPolicy? _smallCandidateExactPolicy;
    private int _rootBatchPairGroups;
    private long _rootBatchPairAdmissionWaitTicks;
    private readonly Channel<SearchCandidate> _candidates;
    private readonly Channel<FamilyTerminalBatch> _terminalHandoff;
    private readonly FamilyExecutionContext _executionContext;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _executionWindowSize;
    private readonly SearchRecoveryLedger _recovery;
    private readonly Task _completion;
    private long _scanned;
    private int _reservedMatches;
    private int _matches;
    private int _confirmedCandidates;
    private int _unverifiedCandidates;
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
    private readonly Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult>? _exactEvaluator;
    private readonly Action<CancellationToken>? _beforeExactEvaluate;
    private readonly Func<int, CancellationToken, ValueTask>? _beforeCandidatePublish;

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
        Func<int, CancellationToken, ValueTask>? beforeCandidatePublish = null,
        SearchContinuationSnapshot? continuation = null,
        Func<FamilyExecutionContext>? executionContextFactory = null,
        Func<Func<Task>, Task>? startRun = null,
        long pendingPayloadLimitBytes = DefaultPendingPayloadLimitBytes,
        Func<SearchPendingCandidate, long>? measurePendingPayload = null,
        Action? recoveryReceiptWaitStarted = null,
        bool allowInitialGpuCreationFallback = false,
        Action<CancellationToken>? beforeExactEvaluate = null)
    {
        if (executionWindowSize <= 0) throw new ArgumentOutOfRangeException(nameof(executionWindowSize));
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        if (pendingPayloadLimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(pendingPayloadLimitBytes));
        _pendingPayloadLimitBytes = pendingPayloadLimitBytes;
        _recoveryReceiptWaitStarted = recoveryReceiptWaitStarted;
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _exactEvaluator = exactEvaluator;
        _beforeExactEvaluate = beforeExactEvaluate;
        _beforeCandidatePublish = beforeCandidatePublish;
        _executionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        _smallCandidateExactPolicy = executionPlan.AllowSmallCandidateExact && !plan.RunOptions.SkipExactValidation
            ? new SmallCandidateExactPolicy() : null;
        _initialGpuCreationFallbackAllowed = allowInitialGpuCreationFallback && !failOnPhysicalRecovery &&
            !plan.RunOptions.SkipExactValidation && executionPlan.RankingAuthority != FamilyPlannerRankingAuthority.UnpricedPhysicalTrial &&
            !executionPlan.DecisionEvidence.Contains("ExplicitPhysicalSelection=true", StringComparer.Ordinal);
        _etaProjection = etaProjection ?? throw new ArgumentNullException(nameof(etaProjection));
        _profile = RuntimeProfileRegistry.Select(plan.Detection);
        _recovery = new(plan, _profile, continuation, measurePendingPayload);
        _families = executionPlan.OrderedFamilies;
        var calibrationCoverage = new HashSet<string>();
        foreach(var family in _families)
        {
            if(family is FamilyCpuExecution cpu)cpu.BindCalibrationCoverage(calibrationCoverage);
            calibrationCoverage.UnionWith(family.Coverage);
        }
        _executionWindowSize = executionWindowSize;
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
            ? new FamilyExecutionContext(requiresGpu: _families.Any(family => family.ConditionPerformance.UsesGpu),
                failOnPhysicalRecovery, cpuWorkerCount: plan.WorkerCount)
            : executionContextFactory();
        _executionContext.SmallCandidateExact = _smallCandidateExactPolicy;
        try { _completion = startRun is null ? Task.Run(RunAsync) : startRun(RunAsync); }
        catch (Exception failure)
        {
            try
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(failure,
                    () => _executionContext.DisposeAsync().AsTask().GetAwaiter().GetResult(),
                    "ProductionSearchSession.Construction");
            }
            finally { _lifetime.Dispose(); }
            throw;
        }
    }

    public Task Completion => _completion;
    public IReadOnlyList<string> FamilyIds => ExecutionPlan.OrderedFamilies.Select(family => family.FamilyId).ToArray();
    // Same-run timing observations never reprice the selected session evidence.
    internal FamilySearchEtaProjectionV1 EtaProjection => Volatile.Read(ref _gpuCreationCpuRecovery)?.Eta ?? _etaProjection;
    internal int ExecutionWindowSize => Volatile.Read(ref _gpuCreationCpuRecovery)?.WindowSize ?? _executionWindowSize;

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
            PendingCandidateCount = _recovery.PendingCount,
            CleanupIssue = Volatile.Read(ref _cleanupIssue),
            ExecutionWarnings = CaptureExecutionWarnings(),
            ObservedScanningSeconds = ElapsedMilliseconds(Math.Max(0,Interlocked.Read(ref _scanLastCompletedTimestamp)-Interlocked.Read(ref _scanFirstCompletedTimestamp)))/1000,
            ObservedScanningRoots = Interlocked.Read(ref _scanAfterFirstRoots)
        };
    }

    public ulong GetSafeNextOrdinal() => _recovery.SafeNextOrdinal;

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
        try
        {
            if (!VisibleSeedCandidateCodec.TryParseOrdinal(_profile, _plan.CanonicalStartSeed, out ulong startOrdinal, out _, out string issue))
                throw new InvalidOperationException("StartSeedOrdinalRejected:" + issue);

            SetSafeCursor(startOrdinal);
            // Only an explicit new session retries retained failures, once each.
            // Retry does not advance the ordinary scan frontier.
            await RetryPendingCandidatesAsync(_lifetime.Token).ConfigureAwait(false);
            await RecoverUnreceivedCandidatesAsync(_lifetime.Token).ConfigureAwait(false);
            LogStartEvidence();

            Task exactConsumer = ConsumeExactBatchesAsync(_lifetime.Token);
            Task familyProducer = ProduceFamilyBatchesAsync(startOrdinal, _lifetime.Token);
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
            _terminalHandoff.Writer.TryComplete();
            bool physicalRecoveryContaminated = Volatile.Read(ref _initialGpuCreationRecoveryAttempted) != 0;
            bool performanceEvidenceUnavailable = false;
            foreach (IFamilyInvocation family in _families)
            {
                try { await family.DisposeAsync(_executionContext).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    RecordCleanupFailure(ex, "FamilyCleanup:" + family.FamilyId);
                    physicalRecoveryContaminated = true;
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
                    performanceEvidenceUnavailable = true;
                    RecordDiagnosticFailure(ex, "PerformanceObservation:" + family.FamilyId);
                }
            }
            long exactAttempts = Interlocked.Read(ref _exactAttempts);
            long completedExactAttempts = Interlocked.Read(ref _completedExactAttempts);
            double exactAggregateMs = ElapsedMilliseconds(Interlocked.Read(ref _exactAggregateStopwatchTicks));
            string exactTimingRevision = "";
            bool exactTimingSubmitted = false;
            // Empty queries return seeds but do not measure normal Exact work.
            if (FamilyExactTimingDomains.HasQueryWork(_plan.CompiledSearch.NormalizedQuery) &&
                !physicalRecoveryContaminated && !performanceEvidenceUnavailable && completedExactAttempts > 0 && exactAggregateMs > 0d)
            {
                try
                {
                    exactTimingRevision = FamilyExactTimingDomains.ImplementationRevision(_plan);
                    SearchPredictabilityVerificationStore.SubmitExactTimingSample(
                    FamilyExactTimingDomains.Resolve(_plan),
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
                $"exactAttempts={exactAttempts};completedExactAttempts={completedExactAttempts};exactAggregateMs={F(exactAggregateMs)};" +
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
            foreach(var family in _families)
                try { family.LogExecutionEvidence(); } catch(Exception ex) { RecordDiagnosticFailure(ex, "ExecutionEvidence:" + family.FamilyId); }
            LogResultEvidence(exactAttempts);
            if (!physicalRecoveryContaminated && !performanceEvidenceUnavailable && (SearchRunState)Volatile.Read(ref _state) != SearchRunState.Faulted)
            {
                try { GpuCostCalibration.Submit(_families.SelectMany(f => f.CostPeaks)); }
                catch (Exception ex) { RecordDiagnosticFailure(ex, "GpuCalibrationSample"); }
                try { CpuCostCalibration.Submit(_families.OfType<FamilyCpuExecution>().SelectMany(f=>f.LocalPeaks)); }
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
                int count = (int)Math.Min(remaining, ExecutionWindowSize);
                var batch = new SearchBatch(batchBase, count);
                var observation = new FamilyObservationWindow(batch, _plan);
                long familyPipelineStarted = Stopwatch.GetTimestamp();
                long pairAdmissionWait = 0;
                FamilyCandidateSet survivors;
                try
                {
                    PreparedFamilyBatch? prepared = preparedNext;
                    preparedNext = null;
                    if (FamilyDenseBatchGroupingPolicy.Enabled && prepared is null && batchNumber > 0 && remaining > count + SmallCandidateExactPolicy.MaximumCandidates &&
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
                                new FamilyObservationWindow(secondBatch, _plan), cancellationToken).ConfigureAwait(false);
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
                        candidates,
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
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = _plan.WorkerCount },
                async (survivorIndex, token) =>
                {
                    if (Volatile.Read(ref _targetReached) != 0) return;
                    ulong logicalOrdinal = candidates.IsDense
                        ? (ulong)survivorIndex
                        : compactAbi1.Span[survivorIndex];
                    if (await EvaluateCandidateAsync(candidates.Batch, logicalOrdinal, token).ConfigureAwait(false))
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
        bool retrying = false)
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
            var candidate = CreateCandidate(input, batch);
            if (!retrying) Interlocked.Increment(ref _scanned);
            Volatile.Write(ref _lastDisposition, (int)SearchDisposition.NotEvaluatedByPolicy);
            RecordOutcome(seed, SearchDisposition.NotEvaluatedByPolicy, string.Empty);
            return await PublishCandidateAsync(candidate, globalCandidate, cancellationToken).ConfigureAwait(false);
        }
        SearchQueryEvaluation evaluation;
        ProductionExactSearchResult exact;
        await _executionContext.EnterCpuAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _exactAttempts);
        long exactAttemptStarted = Stopwatch.GetTimestamp();
        bool exactCompleted = false;
        try
        {
            _beforeExactEvaluate?.Invoke(cancellationToken);
            // Parallel.ForEachAsync owns a child token whose propagation may lag
            // session cancellation. Exact and its consumer must use the same token.
            exact = _exactEvaluator is null
                ? ProductionExactSearchEvaluator.Evaluate(_plan, input, cancellationToken)
                : _exactEvaluator(_plan, input);
            if (exact.Document is { } document)
            {
                ProductionExactSearchEvaluator.ThrowIfPredictionFailed(document);
                string[] exceptions = document.Diagnostics.Where(d => d.Code == PredictionDiagnosticCodes.Exception).Select(d => d.Value).ToArray();
                if (exceptions.Length > 0)
                    throw new InvalidOperationException($"ProductionExact.DocumentException:{document.PredictorId}:{document.CanonicalSeed}:" + string.Join(" | ", exceptions));
            }
            if (exact.Evaluation.Disposition == SearchDisposition.Match && !exact.IsMatch)
                throw new InvalidOperationException("ProductionExact.MatchPayloadMissing:" + seed);
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
            if (evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported)
            {
                bool published = await PublishCandidateAsync(CreateCandidate(input, batch, exact),
                    globalCandidate, cancellationToken).ConfigureAwait(false);
                if (published) _recovery.ResolveCandidate(globalCandidate, published: true);
                return published;
            }
            _recovery.ResolveCandidate(globalCandidate);
            return true;
        }

        var output = CreateCandidate(input, batch, exact);
        if (!await PublishCandidateAsync(output, globalCandidate, cancellationToken).ConfigureAwait(false)) return false;
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
