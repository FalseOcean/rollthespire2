using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Persistence;

internal static partial class SearchFailureContinuationChecks
{
    // Deliberately bounded synthetic faults, not evidence of a player incident.
    // Compare the same command before/after; timings are observational only.
    internal static int RunRecoveryCost() => RunRecoveryCostAsync().GetAwaiter().GetResult();

    private static async Task<int> RunRecoveryCostAsync()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(
            Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(SearchQuery.Empty, context),
            new("000000000000", 512, 1000, 1));
        Check(compiled.Success && compiled.Plan is not null, "cost fixture uses captured context and real execution compiler");
        var plan = compiled.Plan!;
        Console.WriteLine(FormattableString.Invariant(
            $"SEARCH_RECOVERY_CADENCE checkpoint=SearchWorkspaceRuntime.ReceiveEachProcessFrame;workspaceSaveDebounceSeconds={SearchWorkspace.WorkspaceDebounceSeconds};source=Ui/Workspace/SearchWorkspaceRuntime.cs,Ui/Workspace/SearchWorkspace.cs;timingsExcludeDisk=true"));
        CheckRecoverySnapshotFreshness(plan);
        foreach (int failures in new[] { 0, 1000, 10000, 32000 }) MeasureRecoveryPayload(plan, failures);
        await MeasureSessionRecoveryCost(plan, injectFaults: false);
        await MeasureSessionRecoveryCost(plan, injectFaults: true);
        await CheckRepeatedFailureCancellation(plan);
        await CheckRecoveryCapacity(plan);
        MeasureWorkspaceRecoverySave(plan);
        Console.WriteLine($"SEARCH_RECOVERY_COST_PASS checks={_checks};syntheticFaults=true;gpu=false;liveRun=false");
        return 0;
    }

    private static async Task CheckRecoveryCapacity(ExactSearchExecutionRequest original)
    {
        var plan = Recompile(original, original.RunOptions with { ScanCount = 12 });
        const long allowance = 8192;
        SearchContinuationSnapshot full;
        await using (var session = Start(plan, (request, input) => Ordinal(input.CanonicalSeed) == 0
            ? Match(request, input) : throw new IOException("CapacityOriginal:" + new string('x', 10000),
                new InvalidOperationException("CapacityOriginalInner")), payloadLimit: allowance))
        {
            await Finish(session);
            var output = Drain(session);
            Check(session.GetProgress() is { State: SearchRunState.Faulted, MatchCount: 1, PendingCandidateCount: 1 }
                && session.GetProgress().FailureCode.StartsWith("SearchRecoveryCapacityReached:", StringComparison.Ordinal)
                && output.Single().Seed == Seed(0), "one large cause consumes bytes and stops new work, never target credit or a semantic disposition");
            Check(Snapshot(session).NextOrdinal == 0, "capacity stop leaves completed output ACKable and clamps its durable frontier");
            session.AcknowledgeCandidate(output[0].Seed);
            full = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(Snapshot(session)))!;
            Check(full.NextOrdinal == 2 && full.PendingCandidates.Single().Ordinal == 1 &&
                full.PendingCandidates.Single().ExceptionText.Contains("CapacityOriginalInner", StringComparison.Ordinal),
                "capacity stop saves the original chain and failed identity with the ACK-advanced cursor");
        }
        await using (var small = Start(plan, (_, _) => throw new IOException("SmallCapacityCause"), payloadLimit: allowance))
        {
            await Finish(small);
            Check(small.GetProgress().PendingCandidateCount > 1 && small.GetProgress().State == SearchRunState.Faulted,
                "the same byte allowance admits more small causes than large causes, independent of failure count");
        }
        var resumedPlan = Recompile(plan, plan.RunOptions with { StartSeed = Seed(full.NextOrdinal), ScanCount = 3 });
        var skippedPlan = Recompile(resumedPlan, resumedPlan.RunOptions with { SkipExactValidation = true, TargetMatchCount = 2 });
        await using (var skipped = Start(skippedPlan, (_, _) => throw new InvalidOperationException("SkipExactMustNotEvaluate"),
            continuation: full, payloadLimit: allowance))
        {
            await Finish(skipped);
            var output = Drain(skipped);
            var skippedProgress = skipped.GetProgress();
            Console.WriteLine($"SEARCH_RECOVERY_SKIP_CONTROL state={skippedProgress.State};pending={skippedProgress.PendingCandidateCount};confirmed={skippedProgress.MatchCount};unverified={skippedProgress.UnverifiedCandidateCount};failure={skippedProgress.FailureCode};outputs={string.Join(',', output.Select(c => $"{Ordinal(c.Seed)}:{c.ValidationDisposition}:unfiltered={c.IsUnfiltered}"))};samePending={Snapshot(skipped).PendingCandidates.Single() == full.PendingCandidates.Single()}");
            Check(skipped.GetProgress() is { State: SearchRunState.Completed, PendingCandidateCount: 1, MatchCount: 0, UnverifiedCandidateCount: 2 }
                && output.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 2, 3 })
                && output.All(c => c.ValidationDisposition == SearchDisposition.NotEvaluatedByPolicy && c.IsUnfiltered)
                && Snapshot(skipped).PendingCandidates.Single() == full.PendingCandidates.Single(),
                "a full ledger cannot block Owner-skipped Exact raw scanning that adds no CandidateCheck burden; original pending remains unchanged");
        }
        var calls = new ConcurrentDictionary<ulong, int>();
        var receiptWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ackCalls = new ConcurrentDictionary<ulong, int>();
        await using (var awaitingReceipt = Start(resumedPlan, (request, input) =>
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            ackCalls.AddOrUpdate(ordinal, 1, (_, n) => n + 1);
            return ordinal == 1 ? Match(request, input) : Reject(request);
        }, continuation: full, payloadLimit: allowance, receiptWaitStarted: () => receiptWait.TrySetResult()))
        {
            var recovered = await ReadOne(awaitingReceipt);
            await receiptWait.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(!awaitingReceipt.Completion.IsCompleted && ackCalls.Count == 1 &&
                Snapshot(awaitingReceipt).PendingCandidates.Single() == full.PendingCandidates.Single(),
                "receiver latency waits on the real receipt signal without rerunning Exact or erasing its pending cause");
            awaitingReceipt.AcknowledgeCandidate(recovered.Seed);
            await Finish(awaitingReceipt);
            Check(awaitingReceipt.GetProgress() is { State: SearchRunState.Completed, PendingCandidateCount: 0, MatchCount: 1 } &&
                awaitingReceipt.GetProgress().FailureCode.Length == 0 && ackCalls.Keys.Order().SequenceEqual(new ulong[] { 1, 2, 3, 4 }) &&
                ackCalls.Values.All(n => n == 1),
                "delayed ACK releases capacity and automatically admits the ordinary scan with no retry loop or capacity fault");
        }
        var cancelWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var waitingCancelled = Start(resumedPlan, Match, continuation: full, payloadLimit: allowance,
            receiptWaitStarted: () => cancelWait.TrySetResult()))
        {
            _ = await ReadOne(waitingCancelled);
            await cancelWait.Task.WaitAsync(TimeSpan.FromSeconds(10));
            waitingCancelled.Cancel();
            await Finish(waitingCancelled);
            var checkpoint = Snapshot(waitingCancelled);
            Check(waitingCancelled.GetProgress() is { State: SearchRunState.Cancelled, PendingCandidateCount: 1 } &&
                waitingCancelled.GetProgress().FailureCode.Length == 0 && checkpoint.NextOrdinal == full.NextOrdinal &&
                checkpoint.PendingCandidates.Single() == full.PendingCandidates.Single() &&
                checkpoint.UnreceivedOrdinals!.SequenceEqual(new ulong[] { 1 }),
                "cancellation interrupts receipt waiting and preserves the unreceived retry, original failure and trustworthy cursor");
        }
        await using (var drained = Start(resumedPlan, (request, input) =>
        {
            calls.AddOrUpdate(Ordinal(input.CanonicalSeed), 1, (_, n) => n + 1);
            return Reject(request);
        }, continuation: full, payloadLimit: allowance))
        {
            await Finish(drained);
            Check(drained.GetProgress() is { State: SearchRunState.Completed, PendingCandidateCount: 0 } &&
                calls.Keys.Order().SequenceEqual(new ulong[] { 1, 2, 3, 4 }) && calls.Values.All(n => n == 1),
                "initially full resume drains pending before admitting new seeds and never retries automatically");
        }
        calls.Clear();
        await using (var stillFull = Start(resumedPlan, (_, input) =>
        {
            calls.AddOrUpdate(Ordinal(input.CanonicalSeed), 1, (_, n) => n + 1);
            throw new IOException("CapacityRetryCause");
        }, continuation: full, payloadLimit: allowance))
        {
            await Finish(stillFull);
            var pending = Snapshot(stillFull).PendingCandidates.Single();
            Check(calls.Count == 1 && calls.GetValueOrDefault(1UL) == 1 && pending.Attempts == 2 &&
                pending.ExceptionText.Contains("CapacityOriginalInner", StringComparison.Ordinal) &&
                pending.ExceptionText.Contains("CapacityRetryCause", StringComparison.Ordinal) &&
                stillFull.GetProgress().FailureCode.StartsWith("SearchRecoveryCapacityReached:", StringComparison.Ordinal),
                "still-full resume retries existing work once, preserves both causes and blocks only the new scan");
        }
        using var barrier = new Barrier(3);
        var parallelPlan = Recompile(plan, plan.RunOptions with { WorkerCount = 3 });
        await using (var parallel = Start(parallelPlan, (_, input) =>
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("CapacityParallelBarrier");
            throw new IOException("CapacityInflight:" + input.CanonicalSeed);
        }, payloadLimit: 1))
        {
            await Finish(parallel);
            Check(parallel.GetProgress() is { State: SearchRunState.Faulted, PendingCandidateCount: 3 } &&
                Snapshot(parallel).PendingCandidates.All(p => p.ExceptionText.Contains("CapacityInflight:", StringComparison.Ordinal)),
                "all three started parallel failures survive admission stop; overshoot is retained, never dropped");
            Console.WriteLine($"SEARCH_RECOVERY_CAPACITY_INFLIGHT workers=3;retained={parallel.GetProgress().PendingCandidateCount};allowanceBytes=1;byteOvershootMayIncludeWholeInflightRecords=true");
        }
        SearchContinuationSnapshot unmeasured;
        await using (var unaccounted = Start(plan, (request, input) => Ordinal(input.CanonicalSeed) switch
        {
            0 => throw new IOException("OriginalBeforeAccounting", new InvalidOperationException("OriginalBeforeAccountingInner")),
            1 => Match(request, input),
            _ => Reject(request)
        }, payloadLimit: allowance,
            measurePayload: _ => throw new IOException("InjectedAccountingUnavailable")))
        {
            await Finish(unaccounted);
            var pending = Snapshot(unaccounted).PendingCandidates.Single();
            var output = Drain(unaccounted);
            Check(unaccounted.GetProgress() is { State: SearchRunState.Completed, MatchCount: 1, PendingCandidateCount: 1 }
                && unaccounted.GetProgress().FailureCode.Length == 0 && output.Single().Seed == Seed(1)
                && unaccounted.GetProgress().ExecutionWarnings.Any(w => w.StartsWith("SearchRecoveryPayloadAccountingIncomplete:", StringComparison.Ordinal))
                && pending.ExceptionText.Contains("OriginalBeforeAccountingInner", StringComparison.Ordinal)
                && !pending.ExceptionText.Contains("InjectedAccountingUnavailable", StringComparison.Ordinal),
                "optional accounting failure preserves original pending and reports partial coverage while independent search/results continue");
            unaccounted.AcknowledgeCandidate(output[0].Seed);
            unmeasured = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(Snapshot(unaccounted)))!;
        }
        var unmeasuredResume = Recompile(plan, plan.RunOptions with { StartSeed = Seed(unmeasured.NextOrdinal), ScanCount = 1 });
        await using (var resolvedAccounting = Start(unmeasuredResume, (request, _) => Reject(request), continuation: unmeasured,
            payloadLimit: allowance, measurePayload: _ => throw new IOException("InjectedRestoreAccountingUnavailable")))
        {
            await Finish(resolvedAccounting);
            Check(resolvedAccounting.GetProgress() is { State: SearchRunState.Completed, PendingCandidateCount: 0 } &&
                !resolvedAccounting.GetProgress().ExecutionWarnings.Any(w => w.StartsWith("SearchRecoveryPayloadAccountingIncomplete:", StringComparison.Ordinal)),
                "restored accounting fault cannot block a retry and incomplete warning clears when its actual pending record resolves");
        }
        var ledger = new SearchRecoveryLedger(plan, RuntimeProfileRegistry.Select(plan.Detection), null);
        ledger.RetainFailure(0, Seed(0), new IOException("AccountingFirst"));
        long first = ledger.CapturePendingPayload().Bytes;
        ledger.RetainFailure(0, Seed(0), new IOException("AccountingSecond"));
        Check(ledger.CapturePendingPayload().Bytes > first && ledger.PendingCount == 1,
            "replacement accounts the retained retry chain rather than counting another identity");
        ledger.ResolveCandidate(0);
        Check(ledger.CapturePendingPayload() == (0L, null), "resolved pending releases all accounted payload");
        ledger.RetainFailure(0, Seed(0), new IOException(new string('x', 10000)));
        ledger.RetainFailure(1, Seed(1), new IOException(new string('x', 10000)));
        ledger.BeginDelivery(0);
        Check(ledger.CaptureNewCandidateAdmission(allowance).ReceiptChange is null,
            "unresolved failures above allowance stop admission immediately even when another retry awaits ACK");
        ledger.ResolveCandidate(1);
        var change = ledger.CaptureNewCandidateAdmission(allowance).ReceiptChange;
        Check(change is not null && !change.IsCompleted, "a sufficient pending receipt captures a real change task under the ledger lock");
        ledger.AbandonDelivery(0);
        Check(change!.IsCompletedSuccessfully && ledger.CaptureNewCandidateAdmission(allowance).ReceiptChange is null,
            "abandoned delivery wakes admission and exposes its still-unresolved full failure burden");
        ledger.BeginDelivery(0);
        var beforeAck = ledger.CaptureNewCandidateAdmission(allowance).ReceiptChange!;
        ledger.AcknowledgeCandidate(Seed(0));
        Check(beforeAck.IsCompletedSuccessfully && ledger.CaptureNewCandidateAdmission(allowance).Bytes == 0,
            "ACK before the consumer begins awaiting cannot lose the wake-up or retain stale accounted bytes");
    }

    private static void MeasureWorkspaceRecoverySave(ExactSearchExecutionRequest plan)
    {
        const int count = 4000;
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var ledger = new SearchRecoveryLedger(plan, RuntimeProfileRegistry.Select(plan.Detection), null);
        var injected = new IOException("InjectedWorkspaceRecovery:" + new string('x', 256), new IOException("OriginalWorkspaceCause"));
        long accountingTicks = 0;
        for (int i = 0; i < count; i++)
        {
            long started = Stopwatch.GetTimestamp();
            ledger.RetainFailure((ulong)i, Seed((ulong)i), injected);
            accountingTicks += Stopwatch.GetTimestamp() - started;
        }
        ledger.TryAdvanceCursor(count, out _);
        var continuation = ledger.CaptureContinuation()!;
        long approximateRetained = GC.GetTotalMemory(forceFullCollection: true) - before;
        long compactBytes = JsonSerializer.SerializeToUtf8Bytes(continuation).LongLength;
        long accountingAllocated = GC.GetAllocatedBytesForCurrentThread();
        long accountingStarted = Stopwatch.GetTimestamp();
        long countedBytes = 0;
        foreach (var pending in continuation.PendingCandidates.Take(512))
            countedBytes += JsonSerializer.SerializeToUtf8Bytes(pending).LongLength;
        double accountingOnlyMs = Stopwatch.GetElapsedTime(accountingStarted).TotalMilliseconds;
        accountingAllocated = GC.GetAllocatedBytesForCurrentThread() - accountingAllocated;
        Console.WriteLine(FormattableString.Invariant(
            $"SEARCH_RECOVERY_ACCOUNTING samples=512;compactRecordBytes={countedBytes};serializeOnlyMs={accountingOnlyMs:F3};serializeOnlyAllocatedBytes={accountingAllocated};excludesRetentionAndDiagnostics=true"));
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "recovery-capacity-save", Guid.NewGuid().ToString("N"));
        var store = new SearchWorkspace(directory, false);
        var draft = new WorkbenchSearchDraft(plan.CharacterKey, plan.Ascension,
            plan.CompiledSearch.NormalizedQuery, AncientOptionConditionProfile.BroadDefault);
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(plan.CompiledSearch.SemanticFingerprint, draft, null));
        store.SaveCheckpoint(new(SearchRunState.Faulted, count, 0, long.MaxValue, 1000, 0, 0,
            SearchDisposition.NoMatch, "SyntheticRecoveryCapacitySample", false) { PendingCandidateCount = count }, continuation, count);
        long startedFlush = Stopwatch.GetTimestamp();
        store.FlushAll();
        double flushMs = Stopwatch.GetElapsedTime(startedFlush).TotalMilliseconds;
        var options = (JsonSerializerOptions)typeof(SearchWorkspace).GetField("_json",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        long startedSerialize = Stopwatch.GetTimestamp();
        byte[] actualWorkspace = JsonSerializer.SerializeToUtf8Bytes(store.Workspace, options);
        double serializeMs = Stopwatch.GetElapsedTime(startedSerialize).TotalMilliseconds;
        string actualPath = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
        Check(store.LastPersistenceFailure is null && File.Exists(actualPath) &&
            new FileInfo(actualPath).Length == actualWorkspace.LongLength,
            "isolated real workspace flush includes indented history/current continuation representation");
        long startedSave = Stopwatch.GetTimestamp();
        SearchPersistenceFile.WriteAtomic(Path.Combine(directory, "measured-workspace-copy.json"), actualWorkspace);
        double saveMs = Stopwatch.GetElapsedTime(startedSave).TotalMilliseconds;
        var restored = new SearchWorkspace(directory, false);
        Check(restored.DisplayPendingCandidates.Count == count && restored.Workspace.ResultContinuation!.NextOrdinal == count,
            "actual isolated workspace restart preserves all near-capacity pending identities and cursor");
        Console.WriteLine(FormattableString.Invariant(
            $"SEARCH_RECOVERY_WORKSPACE pending={count};compactSnapshotBytes={compactBytes};accountedCompactRecordBytes={ledger.CapturePendingPayload().Bytes};defaultAllowanceBytes={ProductionSearchSession.DefaultPendingPayloadLimitBytes};approxRetainedManagedBytes={approximateRetained};retentionAndAccountingMs={accountingTicks * 1000d / Stopwatch.Frequency:F3};indentedWorkspaceBytes={actualWorkspace.Length};serializeMs={serializeMs:F3};atomicSaveMs={saveMs:F3};fullFlushAllMs={flushMs:F3};memoryIsApproximate=true;diskPath={actualPath}"));
        GC.KeepAlive(ledger);
    }

    private static async Task MeasureSessionRecoveryCost(ExactSearchExecutionRequest plan, bool injectFaults)
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        await using var session = Start(plan, (request, input) =>
        {
            if (injectFaults) throw new IOException("InjectedSessionRecoveryFailure:" + input.CanonicalSeed,
                new InvalidOperationException("InjectedSessionOriginalCause"));
            return Reject(request);
        }, window: 256);
        await Finish(session);
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long processAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        var progress = session.GetProgress();
        var snapshot = Snapshot(session);
        Check(progress.State == SearchRunState.Completed && progress.ScannedCount == 512 &&
            progress.DeliveredCandidateCount == 0 && progress.PendingCandidateCount == (injectFaults ? 512 : 0) &&
            snapshot.NextOrdinal == 512, "session control consumes the same 512 identities without assigning faults target credit");
        Console.WriteLine(FormattableString.Invariant(
            $"SEARCH_RECOVERY_SESSION fault={injectFaults};attempts=512;elapsedMs={elapsedMs:F3};processAllocatedBytes={processAllocatedBytes};pending={progress.PendingCandidateCount};delivered={progress.DeliveredCandidateCount};next={snapshot.NextOrdinal}"));
    }

    private static void MeasureRecoveryPayload(ExactSearchExecutionRequest plan, int failures)
    {
        var ledger = new SearchRecoveryLedger(plan, RuntimeProfileRegistry.Select(plan.Detection), null);
        var injected = new IOException("InjectedRepeatedRecoveryFailure:" + new string('x', 256),
            new InvalidOperationException("InjectedOriginalCause"));
        for (int i = failures - 1; i >= 0; i--) ledger.RetainFailure((ulong)i, Seed((ulong)i), injected);
        ledger.TryAdvanceCursor((ulong)failures + 1, out _);
        var snapshot = ledger.CaptureContinuation()!;
        Check(snapshot.PendingCandidates.Count == failures && snapshot.PendingCandidates.Select(p => p.Ordinal)
            .SequenceEqual(Enumerable.Range(0, failures).Select(i => (ulong)i)), "cost fixture preserves every sorted identity");

        const int captures = 120;
        // Warm-up keeps JIT and serializer metadata initialization out of the sample.
        for (int i = 0; i < 3; i++) ledger.CaptureContinuation();
        _ = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < captures; i++)
        {
            var current = ledger.CaptureContinuation()!;
            // SaveCheckpoint's continuation-change comparison is part of the receipt cost.
            _ = ReferenceEquals(snapshot.PendingCandidates, current.PendingCandidates) ||
                snapshot.PendingCandidates.SequenceEqual(current.PendingCandidates);
            snapshot = current;
        }
        double captureMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long captureBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        started = Stopwatch.GetTimestamp();
        for (int i = 0; i < captures; i++)
        {
            ledger.TryAdvanceCursor((ulong)(failures + i + 2), out _);
            var current = ledger.CaptureContinuation()!;
            _ = ReferenceEquals(snapshot.PendingCandidates, current.PendingCandidates) ||
                snapshot.PendingCandidates.SequenceEqual(current.PendingCandidates);
            snapshot = current;
        }
        double cursorCaptureMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long cursorCaptureBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        const int saves = 3;
        byte[] serialized = [];
        allocated = GC.GetAllocatedBytesForCurrentThread();
        started = Stopwatch.GetTimestamp();
        for (int i = 0; i < saves; i++) serialized = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        double serializeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long serializeBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var roundtrip = JsonSerializer.Deserialize<SearchContinuationSnapshot>(serialized)!;
        Check(roundtrip.PendingCandidates.SequenceEqual(snapshot.PendingCandidates) &&
            roundtrip.NextOrdinal == snapshot.NextOrdinal, "cost snapshot JSON roundtrip retains identities, causes and cursor");
        // A fault stream changes pending at every checkpoint; cache hits alone
        // cannot establish its cost. Keep the loop bounded and identical in both versions.
        allocated = GC.GetAllocatedBytesForCurrentThread();
        started = Stopwatch.GetTimestamp();
        for (int i = 0; i < captures; i++)
        {
            ulong ordinal = (ulong)(failures + i + 2);
            ledger.RetainFailure(ordinal, Seed(ordinal), injected);
            ledger.TryAdvanceCursor((ulong)(failures + captures + i + 2), out _);
            var current = ledger.CaptureContinuation()!;
            _ = ReferenceEquals(snapshot.PendingCandidates, current.PendingCandidates) ||
                snapshot.PendingCandidates.SequenceEqual(current.PendingCandidates);
            snapshot = current;
        }
        double newPendingMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long newPendingBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(snapshot.PendingCandidates.Count == failures + captures && snapshot.PendingCandidates
            .Select(p => p.Ordinal).Distinct().Count() == failures + captures,
            "each changed checkpoint preserves its newly failed identity and all earlier pending work");
        Console.WriteLine(FormattableString.Invariant(
            $"SEARCH_RECOVERY_COST failures={failures};captures={captures};captureMs={captureMs:F3};captureAllocatedBytes={captureBytes};cursorCaptureMs={cursorCaptureMs:F3};cursorCaptureAllocatedBytes={cursorCaptureBytes};newPendingMs={newPendingMs:F3};newPendingAllocatedBytes={newPendingBytes};newPendingFinalCount={snapshot.PendingCandidates.Count};saves={saves};serializeMs={serializeMs:F3};serializeAllocatedBytes={serializeBytes};serializedBytes={serialized.Length}"));
    }

    private static void CheckRecoverySnapshotFreshness(ExactSearchExecutionRequest plan)
    {
        var ledger = new SearchRecoveryLedger(plan, RuntimeProfileRegistry.Select(plan.Detection), null);
        ledger.RetainFailure(7, Seed(7), new IOException("OriginalSeven"));
        ledger.RetainFailure(3, Seed(3), new IOException("OriginalThree"));
        ledger.TryAdvanceCursor(20, out _);
        var original = ledger.CaptureContinuation()!;
        ledger.TryAdvanceCursor(21, out _);
        var advanced = ledger.CaptureContinuation()!;
        Check(original.NextOrdinal == 20 && advanced.NextOrdinal == 21 &&
            original.PendingCandidates.Select(p => p.Ordinal).SequenceEqual(new ulong[] { 3, 7 }),
            "cursor movement cannot mutate an older sorted pending snapshot");
        ledger.BeginDelivery(1);
        var awaiting = ledger.CaptureContinuation()!;
        Check(awaiting.NextOrdinal == 1 && awaiting.UnreceivedOrdinals!.SequenceEqual(new ulong[] { 1 }) &&
            advanced.NextOrdinal == 21 && advanced.UnreceivedOrdinals!.Count == 0,
            "starting delivery invalidates the clamp and delivery snapshot atomically");
        ledger.AcknowledgeCandidate(Seed(1));
        Check(ledger.CaptureContinuation()!.NextOrdinal == 21,
            "receipt invalidates the clamped continuation even with unchanged pending");
        ledger.RetainFailure(3, Seed(3), new IOException("RetryThree"));
        var retry = ledger.CaptureContinuation()!;
        Check(original.PendingCandidates[0].Attempts == 1 && retry.PendingCandidates[0].Attempts == 2 &&
            retry.PendingCandidates[0].ExceptionText.Contains("OriginalThree", StringComparison.Ordinal) &&
            retry.PendingCandidates[0].ExceptionText.Contains("RetryThree", StringComparison.Ordinal),
            "a retry replaces pending evidence while an older captured failure stays immutable");
        ledger.ResolveCandidate(3);
        var resolved = ledger.CaptureContinuation()!;
        Check(resolved.PendingCandidates.Single().Ordinal == 7 && retry.PendingCandidates.Count == 2,
            "a real rejection refreshes pending without modifying the earlier snapshot");
        ledger.BeginDelivery(7);
        ledger.ResolveCandidate(7, published: true);
        Check(ledger.CaptureContinuation()!.PendingCandidates.Single().Ordinal == 7,
            "successful retry remains pending until receipt even after cache reuse");
        ledger.AcknowledgeCandidate(Seed(7));
        var received = ledger.CaptureContinuation()!;
        Check(received.PendingCandidates.Count == 0 && resolved.PendingCandidates.Count == 1,
            "successful retry receipt refreshes pending while its older failure remains frozen");
        ledger.BeginDelivery(9);
        Check(ledger.CaptureContinuation()!.NextOrdinal == 9, "new publication clamps the cached frontier");
        ledger.AbandonDelivery(9);
        Check(ledger.CaptureContinuation()!.NextOrdinal == 21, "abandoned publication invalidates the clamp");
        var restored = new SearchRecoveryLedger(plan, RuntimeProfileRegistry.Select(plan.Detection),
            received with { UnreceivedOrdinals = new ulong[] { 11 } });
        restored.TryAdvanceCursor(21, out _);
        var beforeResolution = restored.CaptureContinuation()!;
        restored.CompleteDeliveryRecovery(11);
        Check(restored.CaptureContinuation()!.UnreceivedOrdinals!.Count == 0 &&
            beforeResolution.UnreceivedOrdinals!.Single() == 11,
            "restored delivery resolution refreshes its cache and retains prior snapshot identity");
    }

    private static async Task CheckRepeatedFailureCancellation(ExactSearchExecutionRequest plan)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = new ConcurrentDictionary<ulong, int>();
        ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest request, TrustedRootHashInput input)
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            attempted.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
            if (ordinal == 128)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("CostFixtureReleaseTimeout");
            }
            if (ordinal is 1 or 2) return Match(request, input);
            throw new IOException("InjectedRepeatedCandidateFailure:" + ordinal,
                new InvalidOperationException("InjectedOriginalCandidateCause:" + ordinal));
        }
        SearchContinuationSnapshot persisted;
        await using (var session = Start(plan, Evaluate, window: 256))
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            session.Cancel();
            release.Set();
            await Finish(session);
            var output = Drain(session);
            Check(output.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 1, 2 }),
                "repeated failures do not hide independent completed results");
            session.AcknowledgeCandidate(output[1].Seed);
            var checkpoint = Snapshot(session);
            Check(session.GetProgress() is { State: SearchRunState.Cancelled, MatchCount: 2, UnverifiedCandidateCount: 0 }
                && checkpoint.NextOrdinal == 1 && checkpoint.PublishedOrdinals.SequenceEqual(new ulong[] { 2 }),
                "cancellation retains real status, unreceived frontier and ACK tail without target credit for failures");
            Check(checkpoint.PendingCandidates.Count == attempted.Count - 2 &&
                checkpoint.PendingCandidates.All(p => p.Attempts == 1 && attempted.GetValueOrDefault(p.Ordinal) == 1 &&
                    p.ExceptionText.Contains("InjectedRepeatedCandidateFailure:" + p.Ordinal, StringComparison.Ordinal) &&
                    p.ExceptionText.Contains("InjectedOriginalCandidateCause:" + p.Ordinal, StringComparison.Ordinal)),
                "all completed failed checks including the in-flight cancellation fault retain original chains exactly once");
            persisted = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(checkpoint))!;
            Check(persisted.PendingCandidates.SequenceEqual(checkpoint.PendingCandidates),
                "cancelled repeated-failure checkpoint survives save/resume serialization");
        }

        var calls = new ConcurrentDictionary<ulong, int>();
        var resumedPlan = Recompile(plan, plan.RunOptions with { StartSeed = Seed(persisted.NextOrdinal) });
        await using var resumed = Start(resumedPlan, (request, input) =>
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            calls.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
            return ordinal == 1 ? Match(request, input) : Reject(request);
        }, continuation: persisted, window: 256);
        await Finish(resumed);
        var recovered = Drain(resumed);
        Check(persisted.PendingCandidates.All(p => calls.GetValueOrDefault(p.Ordinal) == 1) &&
            calls.GetValueOrDefault(2UL) == 0 && recovered.Length == 1 && recovered[0].Seed == Seed(1),
            "explicit resume checks each failed identity once and recovers unreceived output without repeating ACKed output");
        resumed.AcknowledgeCandidate(recovered[0].Seed);
        Check(Snapshot(resumed).PendingCandidates.Count == 0 && resumed.GetProgress().PendingCandidateCount == 0,
            "only actual completed retry outcomes and receipts resolve saved pending obligations");
        Console.WriteLine($"SEARCH_RECOVERY_FAULT_CONTROL attempted={attempted.Count};retained={persisted.PendingCandidates.Count};cancelled=true;delivered=2;receivedTail=2;resumedPending=0;retryMax={calls.Values.Max()}");
    }
}
