using System.Collections.Concurrent;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class GpuOwnerLifecycleChecks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static int Run()
    {
        bool initialization = GpuInitializationChecks.Run();
        bool acceptedWorkFaulted = CheckAcceptedWorkFaultsAtInitializationBoundary();
        bool acceptedCancelledWorkCompleted = CheckAcceptedCancelledWorkTerminates();
        bool concurrentAdmissionCompleted = CheckConcurrentAdmissionVsFaultTerminates();
        bool laterAdmissionFaulted = CheckAdmissionAfterOwnerFaultTerminates();
        bool primaryPreserved = CheckSuccessfulCleanupPreservesPrimary();
        bool cleanupAggregated = CheckCleanupFailurePreservesBothCauses();
        bool allResourcesAttempted = CheckResourceCleanupOrderAndAllFailures();
        bool freeAllReportsFailures = CheckFreeAllReportsEveryReleaseFailure();
        bool constructorOwnership = CheckSessionConstructionOwnership();
        bool consumerCleanupRetained = CheckConsumerCleanupBoundaries();
        bool initialCreationRecovery = CheckInitialCreationRecoveryAsync().GetAwaiter().GetResult();
        bool passed = initialization && acceptedWorkFaulted && acceptedCancelledWorkCompleted &&
                      concurrentAdmissionCompleted && laterAdmissionFaulted && primaryPreserved &&
                      cleanupAggregated && allResourcesAttempted && freeAllReportsFailures &&
                      constructorOwnership && consumerCleanupRetained && initialCreationRecovery;
        Console.WriteLine(
            $"gpu_owner_lifecycle={(passed ? "passed" : "failed")};" +
            $"acceptedWorkFaulted={acceptedWorkFaulted};" +
            $"acceptedCancelledWorkCompleted={acceptedCancelledWorkCompleted};" +
            $"concurrentAdmissionCompleted={concurrentAdmissionCompleted};" +
            $"laterAdmissionFaulted={laterAdmissionFaulted};" +
            $"primaryPreserved={primaryPreserved};cleanupAggregated={cleanupAggregated};" +
            $"allResourcesAttempted={allResourcesAttempted};freeAllReportsFailures={freeAllReportsFailures};" +
            $"constructorOwnership={constructorOwnership};consumerCleanupRetained={consumerCleanupRetained};" +
            $"initialCreationRecovery={initialCreationRecovery}");
        return passed ? 0 : 1;
    }

    private static ExactSearchExecutionRequest LifecycleRequest(string workloadPath)
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + workloadPath);
        return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context),
            workload.Options with { ScanCount = 1, TargetMatchCount = 1, SkipExactValidation = true }).Plan
            ?? throw new InvalidOperationException("Lifecycle fixture did not compile:" + workloadPath);
    }

    private static bool CheckSessionConstructionOwnership()
    {
        var request = LifecycleRequest("v1/cases/S-three-shops.json");
        var plan = FamilyPlanner.InOrder([]);
        var eta = FamilySearchEtaProjectorV1.Project(request, plan);
        var global = FamilyDeviceProfileFoundation.Initialization;
        int activeBefore = global.Capture().ActiveOwners, factories = 0;
        FamilyExecutionContext Context() { factories++; return new(false); }
        foreach (var (window, capacity, parameter) in new[] { (0, 64, "executionWindowSize"), (1, 0, "queueCapacity") })
        {
            try
            {
                _ = new ProductionSearchSession(request, plan, eta, window, capacity, null, "",
                    executionContextFactory: Context);
                return false;
            }
            catch (ArgumentOutOfRangeException failure) when (failure.ParamName == parameter) { }
        }
        try { _ = FamilyExecutionCoordinator.Start(request, queueCapacity: 0, gpuAvailable: false); return false; }
        catch (ArgumentOutOfRangeException failure) when (failure.ParamName == "queueCapacity") { }
        if (factories != 0 || global.Capture().ActiveOwners != activeBefore) return false;

        // No native device is fabricated. Keep a real owner in creation until
        // constructor rollback signals disposal, then verify that it has stopped.
        var state = new FamilyGpuInitialization();
        using var entered = new ManualResetEventSlim();
        FamilyGpuExecutionOwner? owner = null;
        var disposed = typeof(FamilyGpuExecutionOwner).GetField("_disposed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var primary = new InvalidOperationException("InjectedSessionSchedulingFailure");
        bool sawLiveOwner = false;
        try
        {
            _ = new ProductionSearchSession(request, plan, eta, 1, 64, null, "",
                executionContextFactory: () => new(true, gpuOwnerFactory: () => owner = new(() =>
                {
                    entered.Set();
                    SpinWait.SpinUntil(() => owner is not null && (int)disposed.GetValue(owner)! != 0, Timeout);
                    throw new InvalidOperationException("InjectedCreationStoppedAfterConstructorRollback");
                }, state)),
                startRun: _ =>
                {
                    sawLiveOwner = entered.Wait(Timeout) && state.Capture().ActiveOwners == 1;
                    throw primary;
                });
            return false;
        }
        catch (Exception failure)
        {
            return ReferenceEquals(failure, primary) && sawLiveOwner && owner is not null &&
                (int)disposed.GetValue(owner)! == 1 && state.Capture().ActiveOwners == 0 && DisposeWithin(owner);
        }
        finally { if (owner is not null) DisposeWithin(owner); }
    }

    private static bool CheckConsumerCleanupBoundaries()
    {
        foreach (string kind in new[] { "S", "R", "Capsule" })
        {
            var request = LifecycleRequest(kind switch {
                "S" => "v1/cases/S-three-shops.json",
                "R" => "v1/cases/R-three-common.json",
                _ => "family-pricing-matrix-v1/Rfull-Large-RR.json" });
            IFamilyInvocation? family;
            bool created = kind switch {
                "S" => MerchantShopColorlessFamily.TryCreate(request, out family),
                "R" => RelicFamily.TryCreate(request, out family),
                _ => CapsuleRelicFamily.TryCreate(request, out family) };
            if (!created || family is null) throw new InvalidOperationException("Consumer fixture unavailable:" + kind);
            var field = family.GetType().GetField("_gpu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            // The executor is only an ownership marker. A CPU-only context faults
            // before either execute/dispose callback can enter it or call Godot.
            field.SetValue(family, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(field.FieldType));
            var context = new FamilyExecutionContext(false);
            Exception? primary = null;
            var batch = new SearchBatch(0, 1);
            try { family.InvokeAsync(context, new(batch, request), FamilyCandidateSet.Dense(batch), CancellationToken.None)
                .AsTask().WaitAsync(Timeout).GetAwaiter().GetResult(); }
            catch (Exception failure) { primary = failure; }
            if (primary is not InvalidOperationException { Message: "FamilyGpuOwnerUnavailable" } ||
                !family.ExecutionFaulted || field.GetValue(family) is not null)
                throw new InvalidOperationException("Consumer did not retain its original execution fault:" + kind, primary);
            var pending = (System.Collections.Concurrent.ConcurrentQueue<Exception>)typeof(FamilyExecutionContext)
                .GetField("_gpuCleanupFailures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(context)!;
            var cleanup = pending.Single();
            string stage = kind == "Capsule" ? "CapsuleGpuFault.Dispose" : "DisableGpu.Dispose";
            if (cleanup.InnerException is not { Message: "FamilyGpuOwnerUnavailable" } secondary ||
                ReferenceEquals(primary, secondary) || string.IsNullOrWhiteSpace(secondary.StackTrace) ||
                !cleanup.Message.Contains(family.FamilyId, StringComparison.Ordinal) ||
                !cleanup.Message.Contains(stage, StringComparison.Ordinal) ||
                !cleanup.Message.Contains(request.SnapshotFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Consumer cleanup lost original cause or correlation:" + kind);
            family.DisposeAsync(context).AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
            // The existing Session cleanup boundary must receive the deferred
            // second failure while a separately completed calculation stays completed.
            var execution = FamilyPlanner.InOrder([]);
            var session = new ProductionSearchSession(request, execution, FamilySearchEtaProjectorV1.Project(request, execution),
                1, 64, null, "", executionContextFactory: () => context);
            try
            {
                session.Completion.WaitAsync(Timeout).GetAwaiter().GetResult();
                var progress = session.GetProgress();
                if (progress.State != SearchRunState.Completed || progress.FailureCode.Length != 0 ||
                    !progress.CleanupIssue.Contains(stage, StringComparison.Ordinal) ||
                    !progress.CleanupIssue.Contains(secondary.ToString(), StringComparison.Ordinal))
                    throw new InvalidOperationException("Consumer cleanup did not reach independent Session CleanupIssue:" + kind);
            }
            finally { session.DisposeAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult(); }
        }
        return true;
    }

    private static bool CheckSuccessfulCleanupPreservesPrimary()
    {
        var primary = new InvalidOperationException("InjectedPipelineInitializationFailure");
        int cleanupCalls = 0;
        try
        {
            try { ThrowInjectedPrimary(primary); }
            catch (Exception failure)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => cleanupCalls++, "LifecycleCheck");
                throw;
            }
        }
        catch (Exception caught)
        {
            return ReferenceEquals(caught, primary) && cleanupCalls == 1 &&
                   caught.StackTrace?.Contains(nameof(ThrowInjectedPrimary), StringComparison.Ordinal) == true;
        }
        return false;
    }

    private static bool CheckCleanupFailurePreservesBothCauses()
    {
        var primary = new InvalidOperationException("InjectedShaderInitializationFailure");
        var cleanup = new InvalidOperationException("InjectedCleanupFailure");
        try
        {
            try { ThrowInjectedPrimary(primary); }
            catch (Exception failure)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => throw cleanup, "LifecycleCheck");
                throw;
            }
        }
        catch (AggregateException combined)
        {
            return combined.InnerExceptions.Count == 2 &&
                   ReferenceEquals(combined.InnerExceptions[0], primary) &&
                   ReferenceEquals(combined.InnerExceptions[1], cleanup) &&
                   primary.StackTrace?.Contains(nameof(ThrowInjectedPrimary), StringComparison.Ordinal) == true &&
                   !string.IsNullOrWhiteSpace(cleanup.StackTrace);
        }
        catch { return false; }
        return false;
    }

    private static bool CheckResourceCleanupOrderAndAllFailures()
    {
        var primary = new InvalidOperationException("InjectedPrivateChainInitializationFailure");
        var firstFailure = new InvalidOperationException("InjectedFirstBorrowerReleaseFailure");
        var secondFailure = new InvalidOperationException("InjectedSecondBorrowerReleaseFailure");
        var released = new List<string>();
        IDisposable?[] resources =
        [
            new ReleaseProbe("borrower1", released, firstFailure),
            null,
            new ReleaseProbe("borrower2", released, secondFailure),
            new ReleaseProbe("transport", released),
            new ReleaseProbe("module", released)
        ];
        try
        {
            try { ThrowInjectedPrimary(primary); }
            catch (Exception failure)
            {
                FamilyGpuComputeUtility.CleanupAfterFailure(failure,
                    () => PrivateOrdinalBuffer.DisposeAll(resources), "LifecycleCheck");
                throw;
            }
        }
        catch (AggregateException combined)
        {
            if (combined.InnerExceptions.Count != 2 || !ReferenceEquals(combined.InnerExceptions[0], primary) ||
                combined.InnerExceptions[1] is not AggregateException cleanup) return false;
            return released.SequenceEqual(new[] { "borrower1", "borrower2", "transport", "module" }) &&
                   cleanup.InnerExceptions.Count == 2 &&
                   ReferenceEquals(cleanup.InnerExceptions[0], firstFailure) &&
                   ReferenceEquals(cleanup.InnerExceptions[1], secondFailure);
        }
        catch { return false; }
        return false;
    }

    private static bool CheckFreeAllReportsEveryReleaseFailure()
    {
        // A null device faults before entering Godot. This deliberately injects
        // managed release failures without creating or mocking native objects.
        var owned = new List<Godot.Rid> { default, default, default };
        try { FamilyGpuComputeUtility.FreeAll(null!, owned); }
        catch (AggregateException cleanup)
        {
            bool reportedAll = owned.Count == 0 && cleanup.InnerExceptions.Count == 3 &&
                               cleanup.InnerExceptions.All(error => error is NullReferenceException);
            // The emptied ownership list cannot release the same handles again.
            try { FamilyGpuComputeUtility.FreeAll(null!, owned); }
            catch { return false; }
            return reportedAll;
        }
        catch { return false; }
        return false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowInjectedPrimary(Exception primary) => throw primary;

    private sealed class ReleaseProbe(string name, List<string> released, Exception? failure = null) : IDisposable
    {
        public void Dispose()
        {
            released.Add(name);
            if (failure is not null) throw failure;
        }
    }

    private static bool CheckAcceptedWorkFaultsAtInitializationBoundary()
    {
        using var initializationEntered = new ManualResetEventSlim();
        using var releaseInitializationFailure = new ManualResetEventSlim();
        var owner = CreateInitializationBlockedOwner(initializationEntered, releaseInitializationFailure);
        try
        {
            if (!initializationEntered.Wait(Timeout)) return false;
            Task<int> accepted = owner.ExecuteAsync(_ => 1, CancellationToken.None).AsTask();
            releaseInitializationFailure.Set();
            return CompletesWithin(accepted) &&
                   accepted.IsFaulted &&
                   accepted.Exception?.GetBaseException().Message == "InjectedGpuOwnerInitializationFailure" &&
                   DisposeWithin(owner);
        }
        finally
        {
            releaseInitializationFailure.Set();
            DisposeWithin(owner);
        }
    }

    private static bool CheckAcceptedCancelledWorkTerminates()
    {
        using var initializationEntered = new ManualResetEventSlim();
        using var releaseInitializationFailure = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var owner = CreateInitializationBlockedOwner(initializationEntered, releaseInitializationFailure);
        try
        {
            if (!initializationEntered.Wait(Timeout)) return false;
            Task<int> accepted = owner.ExecuteAsync(_ => 1, cancellation.Token).AsTask();
            cancellation.Cancel();
            releaseInitializationFailure.Set();
            return CompletesWithin(accepted) &&
                   (accepted.IsCanceled || accepted.IsFaulted) &&
                   DisposeWithin(owner);
        }
        finally
        {
            releaseInitializationFailure.Set();
            DisposeWithin(owner);
        }
    }

    private static bool CheckConcurrentAdmissionVsFaultTerminates()
    {
        using var initializationEntered = new ManualResetEventSlim();
        using var releaseInitializationFailure = new ManualResetEventSlim();
        var owner = CreateInitializationBlockedOwner(initializationEntered, releaseInitializationFailure);
        try
        {
            if (!initializationEntered.Wait(Timeout)) return false;
            Task<int> accepted = owner.ExecuteAsync(_ => 1, CancellationToken.None).AsTask();
            Task<int> competingAdmission = Task.Run(
                async () => await owner.ExecuteAsync(_ => 2, CancellationToken.None));
            releaseInitializationFailure.Set();
            return CompletesWithin(accepted) && accepted.IsFaulted &&
                   CompletesWithin(competingAdmission) && competingAdmission.IsFaulted &&
                   DisposeWithin(owner);
        }
        finally
        {
            releaseInitializationFailure.Set();
            DisposeWithin(owner);
        }
    }

    private static bool CheckAdmissionAfterOwnerFaultTerminates()
    {
        var owner = new FamilyGpuExecutionOwner(
            () => throw new InvalidOperationException("InjectedGpuOwnerInitializationFailure"));
        try
        {
            if (!DisposeWithin(owner)) return false;
            Task<int> rejected = owner.ExecuteAsync(_ => 1, CancellationToken.None).AsTask();
            return CompletesWithin(rejected) &&
                   rejected.IsFaulted &&
                   rejected.Exception?.GetBaseException().Message == "InjectedGpuOwnerInitializationFailure";
        }
        finally
        {
            DisposeWithin(owner);
        }
    }

    private static FamilyGpuExecutionOwner CreateInitializationBlockedOwner(
        ManualResetEventSlim initializationEntered,
        ManualResetEventSlim releaseInitializationFailure) =>
        new(() =>
        {
            initializationEntered.Set();
            releaseInitializationFailure.Wait(Timeout);
            throw new InvalidOperationException("InjectedGpuOwnerInitializationFailure");
        });

    private static bool CompletesWithin(Task task)
    {
        try
        {
            task.WaitAsync(Timeout).GetAwaiter().GetResult();
        }
        catch
        {
            // Faulted and cancelled are both valid terminal states here.
        }
        return task.IsCompleted;
    }

    private static bool DisposeWithin(FamilyGpuExecutionOwner owner)
    {
        try
        {
            owner.DisposeAsync().AsTask().WaitAsync(Timeout).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal static partial class GpuOwnerLifecycleChecks
{
    private static void RequireCreationRecovery(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("Initial GPU creation recovery: " + reason);
    }

    private static ExactSearchExecutionRequest CreationRecoveryRequest(bool skipExact = false,
        string start = "000000000000", long count = 6)
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/S-three-shops.json");
        return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context),
            workload.Options with { StartSeed = start, ScanCount = count, TargetMatchCount = 100,
                WorkerCount = 1, SkipExactValidation = skipExact }).Plan
            ?? throw new InvalidOperationException("Creation recovery fixture did not compile");
    }

    private static ulong RecoveryOrdinal(string seed)
    {
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, seed, out ulong ordinal, out _, out string issue))
            throw new InvalidOperationException("Creation recovery seed: " + issue);
        return ordinal;
    }

    private static ProductionExactSearchResult UnverifiedRecoveryFixture(ExactSearchExecutionRequest request) =>
        new(null, null, request.Authority, SearchQueryEvaluation.Unknown("InjectedRecoveryObservationUnknown"),
            "InjectedRecoveryObservationUnknown");

    private sealed class CreationRecoveryFamily(string id, bool gpu,
        Func<FamilyExecutionContext, FamilyObservationWindow, FamilyCandidateSet, CancellationToken, ValueTask<FamilyCandidateSet>> invoke,
        Action? dispose = null) : IFamilyInvocation
    {
        public string FamilyId => id;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(id, 65536, [], "CreationRecoveryFixture");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Resolved(id, 1, "CreationRecoveryFixture");
        public FamilyConditionPerformanceProjection ConditionPerformance =>
            new(id, id + ".CreationRecoveryFixture", "Fixture", 1, "Fixture", usesGpu: gpu);
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token) => invoke(context, window, input, token);
        public FamilyPerformanceObservation CapturePerformanceObservation() =>
            new(ConditionPerformance, "Fixture", 0, 0, 0, 0, 0, 0, 0, 0, false, "Fixture");
        public ValueTask DisposeAsync(FamilyExecutionContext context) { dispose?.Invoke(); return ValueTask.CompletedTask; }
    }

    private static async Task<bool> CheckInitialCreationRecoveryAsync()
    {
        try
        {
            await CheckCreationRecoveryCompletionAndContinuation();
            await CheckCreationRecoveryCancellation(afterRecovery: false);
            await CheckCreationRecoveryCancellation(afterRecovery: true);
            await CheckCreationRecoveryExclusions();
            Console.WriteLine("INITIAL_GPU_CREATION_RECOVERY_PASS nativeExecution=false;cpuPrefixOverlap=true;originalQueryRetained=true;pendingAndAck=true");
            return true;
        }
        catch (Exception error)
        {
            Console.WriteLine("INITIAL_GPU_CREATION_RECOVERY_FAIL " + error);
            return false;
        }
    }

    private static async Task CheckCreationRecoveryCompletionAndContinuation()
    {
        var request = CreationRecoveryRequest();
        var creationFailure = new InvalidOperationException("InjectedInitialDeviceCreationFailure",
            new IOException("OriginalNativeWrapperCause"));
        var candidateFailure = new IOException("CandidateFailureAfterCreationRecovery", new Exception("CandidateCause"));
        var initialization = new FamilyGpuInitialization();
        initialization.ObserveEnvironment("CreationRecoveryFixture", true);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var prefixCompleted = new ManualResetEventSlim();
        int creations = 0, dispatches = 0, prefixCalls = 0, disposals = 0;
        var exactCalls = new ConcurrentDictionary<ulong, int>();
        var prefix = new CreationRecoveryFamily("Fixture.CpuPrefix", false, (_, _, input, _) =>
        {
            Interlocked.Increment(ref prefixCalls);
            prefixCompleted.Set();
            // A discarded partial filter must not suppress original-query Exact.
            return ValueTask.FromResult(FamilyCandidateSet.FromAbi1(input.Batch,
                input.EnumerateLogicalOrdinals().Where(i => (i & 1) == 0).ToArray()));
        }, () => Interlocked.Increment(ref disposals));
        var gpu = new CreationRecoveryFamily("Fixture.Gpu", true, (context, _, input, token) =>
            context.ExecuteGpuAsync(_ => { Interlocked.Increment(ref dispatches); return input; }, token),
            () => Interlocked.Increment(ref disposals));
        var selected = FamilyPlanner.InOrder([prefix, gpu]);
        var eta = FamilySearchEtaProjectorV1.Project(request, selected);
        FamilyExecutionContext? executionContext = null;
        await using var session = new ProductionSearchSession(request, selected, eta, 4096, 64, null, "",
            exactEvaluator: (actual, input) =>
            {
                RequireCreationRecovery(ReferenceEquals(actual, request), "Exact receives original complete request");
                ulong ordinal = RecoveryOrdinal(input.CanonicalSeed);
                exactCalls.AddOrUpdate(ordinal, 1, (_, n) => n + 1);
                if (ordinal == 0) throw candidateFailure;
                return UnverifiedRecoveryFixture(actual);
            },
            executionContextFactory: () => executionContext = new(true, cpuWorkerCount: 1,
                gpuOwnerFactory: () => new(() =>
                {
                    Interlocked.Increment(ref creations); entered.Set();
                    if (!release.Wait(Timeout)) throw new TimeoutException("Creation fixture release");
                    throw creationFailure;
                }, initialization)), allowInitialGpuCreationFallback: true);
        try
        {
            RequireCreationRecovery(entered.Wait(Timeout) && prefixCompleted.Wait(Timeout),
                "CPU prefix completes while native creation is still blocked");
            release.Set();
            await session.Completion.WaitAsync(Timeout);
            var rows = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate)) rows.Add(candidate!);
            var progress = session.GetProgress();
            RequireCreationRecovery(progress.State == SearchRunState.Completed && progress.ScannedCount == 6 &&
                progress.PendingCandidateCount == 1 && progress.UnverifiedCandidateCount == 5 && progress.MatchCount == 0,
                "entire frontier completes; failed Exact remains pending, Unknown is unverified");
            RequireCreationRecovery(exactCalls.Count == 6 && exactCalls.Values.All(n => n == 1) &&
                prefixCalls == 1 && creations == 1 && dispatches == 0,
                "all original roots evaluated once; no prefix replay, device retry or GPU action");
            RequireCreationRecovery(disposals == 2 && initialization.Capture() is
                { State: FamilyGpuInitializationState.Failed, ActiveOwners: 0 }, "retired invocations cleaned and failed owner joined");
            RequireCreationRecovery(rows.Count == 5 && rows.All(row => row.IsUnverified && !row.IsUnfiltered &&
                row.ExecutionWarnings.Any(w => w.Contains("replacement=DirectProductionExactCpu", StringComparison.Ordinal))),
                "each delivered result retains explicit recovery warning and actual Exact status");
            RequireCreationRecovery(session.ExecutionPlan.Stages.Count == 0 && session.FamilyIds.Count == 0 &&
                session.ExecutionWindowSize == 256 && session.EtaProjection.Status == FamilySearchEtaStatus.Unavailable &&
                session.EtaProjection.TargetMeanMs is null && session.EtaProjection.TargetStages.Count == 0,
                "active plan, CPU window and ETA no longer advertise native filtering or price");
            RequireCreationRecovery(executionContext!.TryGetInitialGpuCreationFailure(creationFailure, out var source) &&
                ReferenceEquals(source!.Error, creationFailure) &&
                !executionContext.TryGetInitialGpuCreationFailure(new Exception(creationFailure.Message), out _),
                "creation provenance uses original instance rather than matching messages");
            RequireCreationRecovery(RuntimeIssueLedger.Current.Snapshot().Any(issue => issue.Code == "GpuInitializationUnavailable" &&
                issue.ExceptionDetail.Contains("OriginalNativeWrapperCause", StringComparison.Ordinal) &&
                issue.CorrelationId.Contains(request.SnapshotFingerprint, StringComparison.Ordinal)), "original cause and request correlation retained");
            var retained = session.CaptureContinuation()!;
            RequireCreationRecovery(session.GetSafeNextOrdinal() == 6 && retained.NextOrdinal == 1 &&
                retained.PendingCandidates.Single().ExceptionText.Contains("CandidateCause", StringComparison.Ordinal),
                "unACKed delivery clamps durable frontier without replacing candidate failure");
            foreach (var row in rows) session.AcknowledgeCandidate(row.Seed);
            retained = session.CaptureContinuation()!;
            RequireCreationRecovery(retained.NextOrdinal == 6 && retained.PendingCandidates.Single().Attempts == 1,
                "ACK advances frontier while original pending seed survives");

            var resumed = CreationRecoveryRequest(start: Beta110SeedCodec.FormatOrdinal(6), count: 2);
            var resumeCalls = new ConcurrentDictionary<ulong, int>();
            var direct = FamilyPlanner.Plan([]);
            await using var retry = new ProductionSearchSession(resumed, direct,
                FamilySearchEtaProjectorV1.Project(resumed, direct), 2, 64, null, "", continuation: retained,
                executionContextFactory: () => new(false, cpuWorkerCount: 1), exactEvaluator: (actual, input) =>
                {
                    resumeCalls.AddOrUpdate(RecoveryOrdinal(input.CanonicalSeed), 1, (_, n) => n + 1);
                    return UnverifiedRecoveryFixture(actual);
                });
            await retry.Completion.WaitAsync(Timeout);
            var retried = new List<SearchCandidate>();
            while (retry.TryReadCandidate(out var row)) retried.Add(row!);
            RequireCreationRecovery(resumeCalls.Keys.Order().SequenceEqual(new ulong[] { 0, 6, 7 }) &&
                resumeCalls.Values.All(n => n == 1) && retried.Count == 3,
                "resume retries retained failure once and never repeats ACKed recovery outputs");
            foreach (var row in retried) retry.AcknowledgeCandidate(row.Seed);
            RequireCreationRecovery(retry.GetProgress().State == SearchRunState.Completed &&
                retry.CaptureContinuation() is { NextOrdinal: 8, PendingCandidates.Count: 0 }, "resumed ACK closes pending obligation");
        }
        finally { release.Set(); }
    }

    private static async Task CheckCreationRecoveryCancellation(bool afterRecovery)
    {
        var request = CreationRecoveryRequest();
        using var prefixCompleted = new ManualResetEventSlim();
        using var creationEntered = new ManualResetEventSlim();
        using var creationRelease = new ManualResetEventSlim();
        using var exactEntered = new ManualResetEventSlim();
        using var exactRelease = new ManualResetEventSlim();
        int creations = 0, dispatches = 0;
        var prefix = new CreationRecoveryFamily("Fixture.CpuPrefix", false, (_, _, input, _) =>
        { prefixCompleted.Set(); return ValueTask.FromResult(input); });
        var gpu = new CreationRecoveryFamily("Fixture.Gpu", true, (context, _, input, token) =>
            context.ExecuteGpuAsync(_ => { Interlocked.Increment(ref dispatches); return input; }, token));
        var selected = FamilyPlanner.InOrder([prefix, gpu]);
        await using var session = new ProductionSearchSession(request, selected,
            FamilySearchEtaProjectorV1.Project(request, selected), 4096, 64, null, "",
            executionContextFactory: () => new(true, cpuWorkerCount: 1, gpuOwnerFactory: () => new(() =>
            {
                Interlocked.Increment(ref creations);
                creationEntered.Set();
                if (!creationRelease.Wait(Timeout)) throw new TimeoutException("Cancel creation release");
                return null;
            })), exactEvaluator: (actual, _) =>
            {
                exactEntered.Set();
                if (!exactRelease.Wait(Timeout)) throw new TimeoutException("Cancel Exact release");
                return UnverifiedRecoveryFixture(actual);
            }, allowInitialGpuCreationFallback: true);
        try
        {
            RequireCreationRecovery(prefixCompleted.Wait(Timeout), "cancel fixture CPU prefix completes");
            // Prefix completion does not prove the owner thread has entered
            // native initialization. Hold cancellation at the intended boundary.
            RequireCreationRecovery(creationEntered.Wait(Timeout), "cancel fixture native creation entered");
            if (afterRecovery)
            {
                creationRelease.Set();
                RequireCreationRecovery(exactEntered.Wait(Timeout), "CPU Exact begins after creation recovery");
            }
            session.Cancel(); creationRelease.Set(); exactRelease.Set();
            await session.Completion.WaitAsync(Timeout);
            RequireCreationRecovery(session.GetProgress().State == SearchRunState.Cancelled &&
                creations == 1 && dispatches == 0 && session.GetSafeNextOrdinal() == 0,
                $"cancellation preserves unsubmitted/unpublished frontier and does not retry GPU;afterRecovery={afterRecovery};state={session.GetProgress().State};creations={creations};dispatches={dispatches};cursor={session.GetSafeNextOrdinal()}");
            RequireCreationRecovery(afterRecovery == (session.ExecutionPlan.Stages.Count == 0),
                "cancelled pre-recovery startup does not invent a successful fallback");
        }
        finally { creationRelease.Set(); exactRelease.Set(); }
    }

    private static async Task CheckCreationRecoveryExclusions()
    {
        foreach (string mode in new[] { "SkipExact", "Explicit", "FailOnRecovery", "NotAuthorized", "NonCreation" })
        {
            var request = CreationRecoveryRequest(skipExact: mode == "SkipExact");
            var original = new InvalidOperationException("Injected" + mode + "Failure");
            int dispatches = 0, exactCalls = 0;
            var gpu = new CreationRecoveryFamily("Fixture.Gpu", true, (context, _, input, token) => mode == "NonCreation"
                ? ValueTask.FromException<FamilyCandidateSet>(original)
                : context.ExecuteGpuAsync(_ => { Interlocked.Increment(ref dispatches); return input; }, token));
            var selected = FamilyPlanner.InOrder([gpu]);
            if (mode == "Explicit") selected = selected with { RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
                DecisionEvidence = ["ExplicitPhysicalSelection=true"] };
            await using var session = new ProductionSearchSession(request, selected,
                FamilySearchEtaProjectorV1.Project(request, selected), 16, 64, null, "",
                failOnPhysicalRecovery: mode == "FailOnRecovery", allowInitialGpuCreationFallback: mode != "NotAuthorized",
                executionContextFactory: () => mode == "NonCreation" ? new(false) : new(true, gpuOwnerFactory: () => new(() => throw original)),
                exactEvaluator: (actual, _) => { exactCalls++; return UnverifiedRecoveryFixture(actual); });
            await session.Completion.WaitAsync(Timeout);
            RequireCreationRecovery(session.GetProgress().State == SearchRunState.Faulted &&
                ReferenceEquals(session.ExecutionPlan, selected) && dispatches == 0 && exactCalls == 0 &&
                session.GetProgress().FailureCode.Contains(original.Message, StringComparison.Ordinal),
                mode + " retains original failure instead of silently changing execution policy");
        }
    }
}
