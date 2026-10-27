using RolltheSpire2.Ui.Workspace;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Diagnostics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Events;

internal static partial class SearchFailureContinuationChecks
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Search failure continuation: " + message);
        _checks++;
    }

    internal static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(
            Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(SearchQuery.Empty, context),
            new("000000000000", 4, 10, 1));
        Check(compiled.Success && compiled.Plan is not null, "existing captured-context fixture compiles");
        var plan = compiled.Plan!;
        var (continuation, compacted) = await CheckPendingAndAcknowledgement(plan);
        await CheckResume(plan, continuation, failsAgain: false);
        await CheckResume(plan, continuation, failsAgain: true);
        await CheckTwiceResumedUnacknowledgedRetry(plan, compacted);
        await CheckSkippedValidation(plan, continuation);
        await CheckRecompiledPersistenceResume(plan);
        await CheckExplicitOriginPersistence(plan);
        CheckFrozenResultActions(plan);
        await CheckConcurrentReservations(plan);
        await CheckTraceFailure(plan);
        await CheckCleanupFailure(plan);
        await CheckUntrustedFamilyOutput(plan);
        await CheckManualValidationPersistence(plan);
        await CheckManualEventSourceRecapture(plan);
        Console.WriteLine($"SEARCH_FAILURE_CONTINUATION_PASS checks={_checks};gpu=false;liveRun=false;pendingNotDisposition=true");
        return 0;
    }

    private static async Task<(SearchContinuationSnapshot Partial, SearchContinuationSnapshot Compacted)>
        CheckPendingAndAcknowledgement(ExactSearchExecutionRequest plan)
    {
        var original = new IOException("InjectedCandidateCheckFailure", new InvalidOperationException("OriginalCandidateCause"));
        ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest request, TrustedRootHashInput input) =>
            Ordinal(input.CanonicalSeed) switch
            {
                0 => throw original,
                3 => Reject(request),
                _ => Match(request, input)
            };
        await using var session = Start(plan, Evaluate);
        await Finish(session);
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Check((long)typeof(ProductionSearchSession).GetField("_exactAttempts", fields)!.GetValue(session)! == 4 &&
            (long)typeof(ProductionSearchSession).GetField("_completedExactAttempts", fields)!.GetValue(session)! == 3,
            "failed checks remain attempts but cannot calibrate completed Exact work");
        var outputs = Drain(session);
        var progress = session.GetProgress();
        Check(progress.State == SearchRunState.Completed && progress.ScannedCount == 4 &&
            progress.PendingCandidateCount == 1 && progress.MatchCount == 2 &&
            progress.UnverifiedCandidateCount == 0 && progress.DeliveredCandidateCount == 2 &&
            progress.DeliveredCandidateCount < progress.TargetMatchCount,
            "a failed seed remains pending while later normal seeds complete without counting the failure toward target");
        Check(outputs.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 1, 2 }) &&
            outputs.All(c => c.ValidationDisposition == SearchDisposition.Match),
            "the failed seed is never published as Match or ordinary unresolved output");
        var summary = session.GetDiagnosticSummary();
        Check(summary.DispositionCounts.GetValueOrDefault(nameof(SearchDisposition.Match)) == 2 &&
            summary.DispositionCounts.GetValueOrDefault(nameof(SearchDisposition.NoMatch)) == 1 &&
            summary.DispositionCounts.GetValueOrDefault(nameof(SearchDisposition.Unknown)) == 0 &&
            summary.DispositionCounts.GetValueOrDefault(nameof(SearchDisposition.Unsupported)) == 0,
            "only actual model outcomes enter disposition accounting");
        var unacknowledged = Snapshot(session);
        Check(session.GetSafeNextOrdinal() == 4 && unacknowledged.NextOrdinal == 1,
            "durable frontier stays behind an unacknowledged result despite the advanced in-memory scan frontier");
        var pending = unacknowledged.PendingCandidates.Single();
        Check(pending.Ordinal == 0 && pending.Seed == Seed(0) && pending.Stage == "CandidateCheck" &&
            pending.Attempts == 1 && pending.ExceptionText.Contains("InjectedCandidateCheckFailure", StringComparison.Ordinal) &&
            pending.ExceptionText.Contains("OriginalCandidateCause", StringComparison.Ordinal) &&
            pending.QueryFingerprint == plan.CompiledSearch.SemanticFingerprint && pending.SnapshotFingerprint == plan.SnapshotFingerprint,
            "pending preserves original exception chain, seed, stage and frozen request identity");

        // P2 is durably received first. P1 remains unacknowledged, so resume must
        // revisit P1 while remembering that the later result was already received.
        session.AcknowledgeCandidate(Seed(2));
        var partialReceipt = Snapshot(session);
        Check(partialReceipt.NextOrdinal == 1 && partialReceipt.PublishedOrdinals.SequenceEqual(new ulong[] { 2 }) &&
            partialReceipt.PendingCandidates.Single() == pending,
            "received tail is retained independently from the clamped frontier and pending candidate");
        session.AcknowledgeCandidate(Seed(2));
        session.AcknowledgeCandidate(Seed(3));
        Check(JsonSerializer.Serialize(Snapshot(session)) == JsonSerializer.Serialize(partialReceipt),
            "duplicate ACK and ACK of a nonpublication do not resolve pending work or invent a received result");
        var serialized = JsonSerializer.Serialize(partialReceipt);
        var persisted = JsonSerializer.Deserialize<SearchContinuationSnapshot>(serialized)!;
        Check(persisted.NextOrdinal == partialReceipt.NextOrdinal && persisted.PendingCandidates.Single() == pending &&
            persisted.PublishedOrdinals.SequenceEqual(partialReceipt.PublishedOrdinals),
            "continuation serialization retains pending evidence and acknowledged tail identities");
        session.AcknowledgeCandidate(Seed(1));
        var acknowledged = Snapshot(session);
        Check(acknowledged.NextOrdinal == 4 && acknowledged.PendingCandidates.Single() == pending,
            "ACK permits durable frontier advancement without deleting pending work below it");
        bool rejectedInvalidAck = false;
        try { session.AcknowledgeCandidate("invalid delivery seed"); }
        catch (ArgumentException exception) when (exception.Message.StartsWith("SearchDeliverySeedInvalid:", StringComparison.Ordinal))
        { rejectedInvalidAck = true; }
        Check(rejectedInvalidAck && JsonSerializer.Serialize(Snapshot(session)) == JsonSerializer.Serialize(acknowledged),
            "invalid ACK retains its original rejection and cannot mutate the recovery snapshot");
        return (persisted, acknowledged);
    }

    private static async Task CheckResume(ExactSearchExecutionRequest original, SearchContinuationSnapshot continuation, bool failsAgain)
    {
        var calls = new ConcurrentDictionary<ulong, int>();
        ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest request, TrustedRootHashInput input)
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            calls.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
            if (ordinal == 0 && failsAgain) throw new IOException("InjectedRetryFailure");
            return ordinal == 3 ? Reject(request) : Match(request, input);
        }
        var plan = ResumePlan(original, continuation.NextOrdinal);
        await using var session = Start(plan, Evaluate, continuation: continuation);
        await Finish(session);
        var outputs = Drain(session);
        Check(session.GetProgress().State == SearchRunState.Completed && calls.GetValueOrDefault(0UL) == 1 &&
            calls.GetValueOrDefault(1UL) == 1 && calls.GetValueOrDefault(2UL) == 0 && calls.GetValueOrDefault(3UL) == 1,
            "explicit resume retries pending exactly once and never re-evaluates an acknowledged later seed");
        Check(outputs.Select(c => Ordinal(c.Seed)).SequenceEqual(failsAgain ? new ulong[] { 1 } : new ulong[] { 0, 1 }),
            "resume republishes the unacknowledged result and excludes the already received tail");
        var snapshot = Snapshot(session);
        if (failsAgain)
        {
            var pending = snapshot.PendingCandidates.Single();
            Check(pending.Ordinal == 0 && pending.Attempts == 2 &&
                pending.FirstFailedAtUtc == continuation.PendingCandidates.Single().FirstFailedAtUtc &&
                pending.ExceptionText.Contains("InjectedRetryFailure", StringComparison.Ordinal) &&
                session.GetProgress().PendingCandidateCount == 1 && session.GetProgress().MatchCount == 1,
                "another failed explicit retry increases Attempts, retains first failure time and remains outside target progress");
        }
        else
        {
            Check(snapshot.PendingCandidates.Single() == continuation.PendingCandidates.Single() &&
                session.GetProgress().PendingCandidateCount == 1 &&
                session.GetProgress().MatchCount == 2 && snapshot.NextOrdinal == 1,
                "unacknowledged retry stays pending while the ordinary unacknowledged result alone clamps the frontier");
        }
        foreach (var output in outputs) session.AcknowledgeCandidate(output.Seed);
        Check(Snapshot(session).NextOrdinal == 4 && Snapshot(session).PendingCandidates.Count == (failsAgain ? 1 : 0),
            "resumed ACKs preserve the independent pending obligation");
    }

    private static async Task CheckTwiceResumedUnacknowledgedRetry(ExactSearchExecutionRequest original,
        SearchContinuationSnapshot compacted)
    {
        Check(compacted.NextOrdinal == 4 && compacted.PublishedOrdinals.Count == 0 &&
            compacted.PendingCandidates.Single().Ordinal == 0,
            "first receipt compacts ACKed results 1/2 below frontier 4 while retaining failed seed 0");
        var firstCalls = new ConcurrentDictionary<ulong, int>();
        ProductionExactSearchResult FirstRetry(ExactSearchExecutionRequest request, TrustedRootHashInput input)
        {
            firstCalls.AddOrUpdate(Ordinal(input.CanonicalSeed), 1, (_, count) => count + 1);
            return Match(request, input);
        }
        var firstPlan = Recompile(original, original.RunOptions with { StartSeed = Seed(4), ScanCount = 1, TargetMatchCount = 1 });
        SearchContinuationSnapshot unreceivedRetry;
        await using (var first = Start(firstPlan, FirstRetry, continuation: compacted))
        {
            await Finish(first);
            var output = Drain(first);
            Check(first.GetProgress().State == SearchRunState.Completed && output.Length == 1 &&
                output[0].Seed == Seed(0) && firstCalls.Count == 1 && firstCalls.GetValueOrDefault(0UL) == 1,
                "first restore succeeds on its pending retry and stops at target without replaying the compacted prefix");
            unreceivedRetry = Snapshot(first);
            Check(unreceivedRetry.NextOrdinal == 4 && unreceivedRetry.PublishedOrdinals.Count == 0 &&
                unreceivedRetry.PendingCandidates.Single() == compacted.PendingCandidates.Single(),
                "unACKed retry preserves pending 0 without rewinding frontier into forgotten ACKs 1/2");
        }

        unreceivedRetry = JsonSerializer.Deserialize<SearchContinuationSnapshot>(JsonSerializer.Serialize(unreceivedRetry))!;
        var secondCalls = new ConcurrentDictionary<ulong, int>();
        ProductionExactSearchResult SecondRetry(ExactSearchExecutionRequest request, TrustedRootHashInput input)
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            secondCalls.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
            return ordinal is 3 or 4 ? Reject(request) : Match(request, input);
        }
        // Keep the ordinary scan's end fixed at 5. A regressed frontier of 0
        // would actually re-evaluate and re-publish the lost ACK prefix 1/2.
        var secondPlan = Recompile(original, original.RunOptions with { StartSeed = Seed(unreceivedRetry.NextOrdinal),
            ScanCount = checked(5 - (long)unreceivedRetry.NextOrdinal) });
        await using var second = Start(secondPlan, SecondRetry, continuation: unreceivedRetry);
        await Finish(second);
        var secondOutput = Drain(second);
        Check(second.GetProgress().State == SearchRunState.Completed && secondCalls.Count == 2 &&
            secondCalls.GetValueOrDefault(0UL) == 1 && secondCalls.GetValueOrDefault(4UL) == 1 &&
            secondOutput.Length == 1 && secondOutput[0].Seed == Seed(0),
            "second restore only retries 0 and starts ordinary scanning at 4, never re-publishing ACKed results 1/2");
        Check(Snapshot(second).NextOrdinal == 5 &&
            Snapshot(second).PendingCandidates.Single() == compacted.PendingCandidates.Single(),
            "second successful but unACKed retry remains recoverable after new ordinary scanning completes");
        second.AcknowledgeCandidate(Seed(0));
        Check(Snapshot(second).NextOrdinal == 5 && Snapshot(second).PendingCandidates.Count == 0 &&
            second.GetProgress().PendingCandidateCount == 0,
            "ACK of the retry finally removes pending 0 without moving the ordinary frontier backward");
    }

    private static async Task CheckSkippedValidation(ExactSearchExecutionRequest original, SearchContinuationSnapshot continuation)
    {
        // A conservative earlier frontier exercises the old failed ordinal in
        // ordinary scanning too; SkipExact must not publish it as a raw result.
        continuation = continuation with { NextOrdinal = 0 };
        var plan = Recompile(original, original.RunOptions with { StartSeed = Seed(0),
            SkipExactValidation = true, TargetMatchCount = 2 });
        int attempts = 0;
        await using var session = Start(plan, (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new Exception("SkipExact must not invoke Exact");
        }, continuation: continuation);
        await Finish(session);
        var outputs = Drain(session);
        var progress = session.GetProgress();
        Check(progress.State == SearchRunState.Completed && attempts == 0 && progress.MatchCount == 0 &&
            progress.UnverifiedCandidateCount == 2 && progress.PendingCandidateCount == 1 &&
            outputs.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 1, 3 }) &&
            outputs.All(c => c.ValidationDisposition == SearchDisposition.NotEvaluatedByPolicy),
            "SkipExact retains the old failed seed, skips the acknowledged tail and reaches target only with new actual outputs");
        Check(Snapshot(session).PendingCandidates.Single() == continuation.PendingCandidates.Single(),
            "skipped checking neither resolves nor retries the old pending obligation");
    }

    private static async Task CheckConcurrentReservations(ExactSearchExecutionRequest original)
    {
        var bothReserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reservationIds = new ConcurrentBag<int>();
        int reservations = 0;
        var plan = original with { RunOptions = original.RunOptions with { WorkerCount = 3, TargetMatchCount = 2 } };
        ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest request, TrustedRootHashInput input)
        {
            if (Ordinal(input.CanonicalSeed) != 0) return Match(request, input);
            bothReserved.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            failureEntered.TrySetResult();
            throw new IOException("InjectedConcurrentFrontFailure");
        }
        async ValueTask BeforePublish(int reservation, CancellationToken token)
        {
            reservationIds.Add(reservation);
            if (Interlocked.Increment(ref reservations) == 2) bothReserved.TrySetResult();
            await failureEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        await using var session = Start(plan, Evaluate, beforePublish: BeforePublish);
        await Finish(session);
        var outputs = Drain(session);
        Check(reservations == 2 && reservationIds.Order().SequenceEqual(new[] { 1, 2 }) &&
            outputs.Length == 2 && outputs.Select(c => c.Seed).Distinct().Count() == 2 &&
            outputs.All(c => Ordinal(c.Seed) != 0) && session.GetProgress().MatchCount == 2 &&
            session.GetProgress().PendingCandidateCount == 1 && session.GetProgress().State == SearchRunState.Completed,
            "concurrent front failure cannot consume a target reservation or lose later successful publications");
        Check(Snapshot(session).PendingCandidates.Single().ExceptionText.Contains("InjectedConcurrentFrontFailure", StringComparison.Ordinal),
            "target cancellation does not erase the concurrent real candidate failure");
    }

    private static async Task CheckTraceFailure(ExactSearchExecutionRequest original)
    {
        var sink = new ThrowingTraceSink();
        var plan = original with { ResolvedScanCount = 1 };
        await using var session = Start(plan, Match, sink: sink, traceSeed: Seed(0));
        await Finish(session);
        Check(sink.Calls > 0 && Drain(session).Length == 1 && session.GetProgress() is
            { State: SearchRunState.Completed, MatchCount: 1, PendingCandidateCount: 0 },
            "a genuinely throwing trace sink is exercised without taking ownership of result delivery");
    }

    private static async Task CheckCleanupFailure(ExactSearchExecutionRequest original)
    {
        var family = new FixtureFamily(cleanupFails: true);
        var plan = original with { ResolvedScanCount = 1 };
        await using var session = Start(plan, Match, family: family);
        await Finish(session);
        Check(Drain(session).Length == 1 && session.GetProgress() is
            { State: SearchRunState.Completed, MatchCount: 1, PendingCandidateCount: 0 } progress &&
            progress.CleanupIssue.Contains("InjectedFamilyCleanupFailure", StringComparison.Ordinal),
            "cleanup failure preserves computation Completed and delivered results with a separate CleanupIssue");
    }

    private static async Task CheckUntrustedFamilyOutput(ExactSearchExecutionRequest original)
    {
        var allowFault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var family = new FixtureFamily(untrustedSecondBatch: allowFault.Task);
        var plan = original with { ResolvedScanCount = 2 };
        await using var session = Start(plan, Match, family: family, window: 1);
        try
        {
            var first = await ReadOne(session);
            Check(first.Seed == Seed(0), "first batch actually published before the later Family identity fault");
            session.AcknowledgeCandidate(first.Seed);
            allowFault.TrySetResult();
            await Finish(session);
            var progress = session.GetProgress();
            Check(progress.State == SearchRunState.Faulted && progress.MatchCount == 1 &&
                progress.PendingCandidateCount == 0 && progress.FailureCode.Contains("Family changed SearchBatch identity", StringComparison.Ordinal) &&
                Drain(session).Length == 0 && Snapshot(session).NextOrdinal == 1,
                "untrusted Family output still faults affected execution while retaining its already delivered first result");
        }
        finally { allowFault.TrySetResult(); }
    }

    private static void Checkpoint(SearchWorkspace store, IProductionSearchSession session, bool complete)
    {
        var continuation = session.CaptureContinuation();
        ulong next;
        bool safe = continuation is not null ? (next = continuation.NextOrdinal) <= Beta110SeedCodec.SpaceSize : session.TryGetSafeNextOrdinal(out next);
        store.SaveCheckpoint(session.GetProgress(), continuation, safe ? next : null, complete && safe && next == Beta110SeedCodec.SpaceSize);
    }

    private static void CheckFrozenResultActions(ExactSearchExecutionRequest original)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "frozen-result-actions", Guid.NewGuid().ToString("N"));
        var store = new SearchWorkspace(directory, false);
        var environment = new SearchEnvironmentSignature { CaptureComplete = true, Fingerprint = "frozen-original-env" };
        store.EnsureEnvironment(environment);
        var draft = new WorkbenchSearchDraft(original.CharacterKey, original.Ascension, original.CompiledSearch.NormalizedQuery,
            AncientOptionConditionProfile.BroadDefault);
        var players = new SeedLibraryPlayer[] { new(0, original.CharacterKey,
            JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>("{}")!, "original-unlocks",
            AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null) };
        var context = new SeedLibraryContext(original.Detection.NormalizedVersion, original.ProfileId,
            WorldGameMode.Singleplayer, original.Ascension, players);
        Check(store.TrySetPersistentOrigin(Seed(5), out _, out _), "frozen start accepts explicit origin");
        var prepared = store.PrepareSearchStart(draft, original, context);
        var first = prepared.Batch.Start!;
        Check(first.Plan.CanonicalStartSeed == Seed(5) && first.Resolution.ExplicitOriginChangeId == store.Cursor.PendingOriginChangeId &&
            store.Workspace.ResultBatchId.Length == 0, "prepare freezes resolved plan and origin without installing or consuming them");
        players[0] = players[0] with { UnlockSource = "edited-after-prepare" };
        environment.Fingerprint = "edited-after-prepare";
        Check(first.Context!.Players[0].UnlockSource == "original-unlocks" && first.Environment.Fingerprint == "frozen-original-env",
            "start context is detached from the original context/environment objects");
        // A successful older start may consume only its own frozen setting token.
        Check(store.TrySetPersistentOrigin(Seed(7), out _, out _), "newer origin accepted after preparation");
        string newer = store.Cursor.PendingOriginChangeId;
        store.BindPreparedResultWorkspace(prepared);
        Check(store.Cursor.PendingOriginChangeId == newer && newer != first.Resolution.ExplicitOriginChangeId,
            "binding cannot consume a newer explicit origin token");
        // Restore the same captured environment; the mutable fixture above is no longer the runtime observation.
        store.EnsureEnvironment(first.Environment.Snapshot());
        SearchCandidate Candidate(ExactSearchExecutionRequest plan, ulong ordinal, string route)
        {
            var exact = Match(plan, TrustedRootHashInput.FromCanonicalSeed(RuntimeProfileRegistry.Select(plan.Detection), Seed(ordinal)));
            return new(Seed(ordinal), plan.ProfileId, plan.CharacterKey, plan.Ascension, plan.SnapshotFingerprint,
                exact.Request!, exact.Document, exact.Authority, exact.Evaluation.Evidence,
                exact.Evaluation.MatchedRouteIds, [new(Seed(ordinal), route, [], [], "", null, [])]);
        }
        for (ulong i = 0; i < 35; i++) store.AppendResult(Candidate(first.Plan, i, "choice.1.first"), first.Plan.CompiledSearch.SemanticFingerprint);
        var originalRow = store.DisplayResults.Single(row => row.Seed == Seed(34));
        var action = store.ResultAction(originalRow);
        Check(action.Live is { } live && ReferenceEquals(live.Start.Plan, first.Plan) &&
            action.Context!.Players[0].Selection?.Route == "first" && action.Association?.SearchRecordId == first.HistoryId,
            "row action owns the original live plan/witness/context/history");
        var other = draft with { Ascension = original.Ascension == 9 ? 8 : 9 };
        store.ActivateQueryResults(other);
        Check(store.DisplayResults.Count == 0 && action.HistoryId == first.HistoryId && action.Draft!.Ascension == original.Ascension,
            "changing the active editor/query cannot relabel a captured action");
        store.ActivateQueryResults(draft);
        Check(store.DisplayResults.Count == 35 && store.ResultAction(store.DisplayResults.Single(row => row.Seed == Seed(0))).Live is not null,
            "returning to a query retains all process rows and live payload beyond the disk quota");
        var secondPlan = Recompile(original, new(Seed(7), 40, 40, 1));
        var next = store.PrepareSearchStart(draft, secondPlan, first.Context);
        store.BindPreparedResultWorkspace(next);
        store.AppendResult(Candidate(next.Batch.Start!.Plan, 34, "choice.2.second"), secondPlan.CompiledSearch.SemanticFingerprint);
        var replacement = store.ResultAction(store.DisplayResults.Single(row => row.Seed == Seed(34)));
        var staleProjection = store.ResultAction(originalRow);
        Check(staleProjection.Live is null && staleProjection.Context!.Players[0].Selection?.Route == "first" &&
            staleProjection.Association == action.Association, "an older row reference cannot borrow the replacement observation's live payload");
        Check(replacement.BatchId != action.BatchId && replacement.Live!.Start.Plan == next.Batch.Start.Plan &&
            action.Live!.Start.Plan == first.Plan && action.Live.Candidate.Witnesses.Single().OpeningRouteId == "choice.1.first" &&
            action.Context!.Players[0].Selection?.Route == "first" && replacement.Context!.Players[0].Selection?.Route == "second",
            "same-history repeated seed replaces display while previously captured action keeps its actual observation/batch/plan");
        var uncheckedCandidate = Candidate(next.Batch.Start.Plan, 34, "stale") with
            { ValidationDisposition = SearchDisposition.NotEvaluatedByPolicy, Document = null, Witnesses = [] };
        store.AppendResult(uncheckedCandidate, secondPlan.CompiledSearch.SemanticFingerprint);
        Check(store.ResultAction(store.DisplayResults.Single(row => row.Seed == Seed(34))).Live!.Candidate.Witnesses.Single().OpeningRouteId == "choice.2.second",
            "a later unchecked duplicate cannot replace a retained confirmed observation's live payload");
        store.FlushAll();
        var reopened = new SearchWorkspace(directory, false);
        Check(reopened.DisplayResults.Count == 30 && reopened.ResultAction(reopened.DisplayResults.Last()).Live is null &&
            store.DisplayResults.Count == 35 && store.ResultBatch!.LiveResults.Count == 35,
            "restart loads bounded persisted observations without plans; process display and live payload remain unbounded");
    }

    private static async Task CheckManualValidationPersistence(ExactSearchExecutionRequest original)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "manual-validation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new SearchWorkspace(directory, false);
        store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "manual-original-env" });
        var draft = new WorkbenchSearchDraft(original.CharacterKey, original.Ascension, original.CompiledSearch.NormalizedQuery,
            AncientOptionConditionProfile.BroadDefault);
        var context = new SeedLibraryContext(original.Detection.NormalizedVersion, original.ProfileId, WorldGameMode.Singleplayer,
            original.Ascension, [new(0, original.CharacterKey,
                JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>("{}")!, "captured-original-unlocks",
                AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(original.CompiledSearch.SemanticFingerprint, draft, context, skipExactValidation: true));
        void AddUnverified(SearchWorkspace target, string seed) => target.AppendResult(new(seed, original.ProfileId,
            original.CharacterKey, original.Ascension, original.SnapshotFingerprint, null!, null, original.Authority, [], [], [])
            { ValidationDisposition = SearchDisposition.NotEvaluatedByPolicy, ValidationIssue = "original-skipped-check", IsUnfiltered = true },
            original.CompiledSearch.SemanticFingerprint);
        AddUnverified(store, Seed(0));
        store.SaveResultProgress(new(SearchRunState.Completed, 4, 0, 4, 10, 1, 1,
            SearchDisposition.NotEvaluatedByPolicy, "", false) { UnverifiedCandidateCount = 1 });
        store.FlushAll();
        string originalRow = JsonSerializer.Serialize(store.DisplayResults.Single());
        var manual = ExactSearchExecutionRequestFactory.Compile(original.CompiledSearch, new(Seed(0), 1, 1, 1)).Plan!;
        Check(manual.SnapshotFingerprint != original.SnapshotFingerprint,
            "a manual check keeps its actual one-seed execution fingerprint instead of borrowing a former scan identity");
        Check(store.TryManualValidationSource(Seed(0), out var history, out var row, out _), "original saved manual source is available");
        var failure = new InvalidOperationException("manual-outer-failure", new IOException("manual-original-inner-cause"));
        var failed = store.Runtime.TrackManualCandidateValidation(history, row, manual, () => throw failure);
        try { await failed.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
        // Only the receiving owner polls. The initiating UI is absent.
        store.Runtime.PollManualCandidateValidations();
        var pending = SearchWorkspace.PendingForHistory(history).Single();
        Check(failed.Accepted && !store.Runtime.HasManualCandidateValidation && pending.Ordinal == 0 && pending.Attempts == 1 &&
            pending.ExceptionText.Contains(failure.ToString(), StringComparison.Ordinal) &&
            pending.QueryFingerprint == original.CompiledSearch.SemanticFingerprint && pending.SnapshotFingerprint == manual.SnapshotFingerprint &&
            JsonSerializer.Serialize(store.DisplayResults.Single()) == originalRow &&
            store.Workspace.ResultProgress is { MatchCount: 0, UnverifiedCandidateCount: 1 },
            "manual fault persists original cause/identity independently, leaves the old unverified row untouched and receives no target credit");

        var pendingAction = store.PendingResultAction(history, pending);
        Check(pendingAction?.ValidationPlan == manual && pendingAction.HistoryId == history.Id &&
            pendingAction.Result.ValidationIssue == "original-skipped-check", "process pending action retains the original failed attempt plan and original observation");
        store = new(directory, false);
        Check(store.TryManualValidationSource(Seed(0), out history, out row, out _) &&
            SearchWorkspace.PendingForHistory(history).Single() == pending,
            "saved pending reopens with its original result query, context and exception");
        Check(store.PendingResultAction(history, pending) is { ValidationPlan: null, Live: null },
            "restart pending has only its actual saved context and does not fabricate a live plan");
        store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "manual-other-env" });
        Check(!store.TryManualValidationSource(Seed(0), out _, out _, out var environmentIssue) && environmentIssue.Length > 0 &&
            store.PendingHistories.SelectMany(SearchWorkspace.PendingForHistory).Single() == pending,
            "an environment mismatch retains the original pending instead of relabeling it as resolved");
        store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "manual-original-env" });
        Check(store.TryManualValidationSource(Seed(0), out history, out row, out _), "the original matching environment can retry its own pending");
        var retryPlan = ExactSearchExecutionRequestFactory.Compile(original.CompiledSearch, new(Seed(0), 1, 1, 2)).Plan!;
        var retryFailure = new IOException("manual-explicit-retry-failure");
        var retry = store.Runtime.TrackManualCandidateValidation(history, row, retryPlan, () => throw retryFailure);
        try { await retry.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException exception) when (ReferenceEquals(exception, retryFailure)) { }
        store.Runtime.PollManualCandidateValidations();
        var twice = SearchWorkspace.PendingForHistory(history).Single();
        Check(retry.Accepted && twice.Attempts == 2 && twice.FirstFailedAtUtc == pending.FirstFailedAtUtc &&
            twice.SnapshotFingerprint == pending.SnapshotFingerprint && retry.Plan.SnapshotFingerprint == retryPlan.SnapshotFingerprint &&
            twice.ExceptionText.Contains(failure.ToString(), StringComparison.Ordinal) &&
            twice.ExceptionText.Contains(retryFailure.ToString(), StringComparison.Ordinal) &&
            twice.ExceptionText.Contains("attemptSnapshot=" + retryPlan.SnapshotFingerprint, StringComparison.Ordinal) &&
            JsonSerializer.Serialize(store.DisplayResults.Single()) == originalRow,
            "a failed explicit retry retains the origin obligation and both causes while recording the new attempt's genuine identity");

        var providerCancellation = new OperationCanceledException("manual-provider-thrown-oce", new IOException("manual-provider-original-cause"));
        var canceledByProvider = store.Runtime.TrackManualCandidateValidation(history, row, retryPlan, () => throw providerCancellation);
        try { await canceledByProvider.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
        store.Runtime.PollManualCandidateValidations();
        twice = SearchWorkspace.PendingForHistory(history).Single();
        Check(canceledByProvider.Accepted && twice.Attempts == 3 &&
            twice.ExceptionText.Contains("manual-provider-thrown-oce", StringComparison.Ordinal) &&
            twice.ExceptionText.Contains("manual-provider-original-cause", StringComparison.Ordinal) &&
            JsonSerializer.Serialize(store.DisplayResults.Single()) == originalRow,
            "provider-thrown cancellation without a user cancellation source remains a retained execution fault with its original cause");

        var input = TrustedRootHashInput.FromCanonicalSeed(RuntimeProfileRegistry.Select(original.Detection), Seed(0));
        int exactCalls = 0;
        var success = store.Runtime.TrackManualCandidateValidation(history, row, retryPlan, () =>
            { Interlocked.Increment(ref exactCalls); return Match(retryPlan, input); });
        await success.Task.WaitAsync(TimeSpan.FromSeconds(10));
        string workspaceFile = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
        long deliveryNow = Stopwatch.GetTimestamp();
        RolltheSpire2.Ui.Workspace.SearchWorkspaceRuntime.ManualCandidateValidation? otherDelivery = null;
        int otherCalls = 0;
        using (var locked = new FileStream(workspaceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var environmentLocked = new FileStream(Path.Combine(directory, "RolltheSpire2", "state", "search_environment.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Runtime.PollManualCandidateValidations(deliveryNow);
            Check(!success.Accepted && store.Runtime.HasManualCandidateDelivery(Seed(0)) && !store.Runtime.HasSearchInFlight && store.HasUnsavedSearchState &&
                SearchWorkspace.PendingForHistory(history).Single() == twice &&
                store.DisplayResults.Single().EffectiveValidationDisposition == SearchDisposition.Match,
                "failed delivery save retains both the new in-memory observation and original pending without rerunning the worker");
            for (int frame = 0; frame < 100; frame++) store.Runtime.PollManualCandidateValidations(deliveryNow);
            Check(success.DeliveryAttempts == 1 && success.NextDeliveryAttemptAt > deliveryNow && exactCalls == 1 &&
                !store.AuxiliaryFailures.Any(detail => detail.Contains("Persistence:search_environment.json", StringComparison.Ordinal)),
                "a hundred immediate owner polls do not retry I/O or Exact and manual delivery does not rewrite environment state");
            bool duplicateBlocked = false;
            try { store.Runtime.TrackManualCandidateValidation(history, row, retryPlan, () => throw new Exception("Duplicate origin must not execute")); }
            catch (InvalidOperationException exception) when (exception.Message == "ManualValidationAwaitingDelivery") { duplicateBlocked = true; }
            Check(duplicateBlocked, "the same seed and original history cannot start another check while its actual result awaits delivery");

            AddUnverified(store, Seed(2));
            Check(store.TryManualValidationSource(Seed(2), out var otherHistory, out var otherRow, out _),
                "another seed retains its own valid source while a former result awaits storage");
            var otherPlan = ExactSearchExecutionRequestFactory.Compile(original.CompiledSearch, new(Seed(2), 1, 1, 1)).Plan!;
            otherDelivery = store.Runtime.TrackManualCandidateValidation(otherHistory, otherRow, otherPlan, () =>
            {
                Interlocked.Increment(ref otherCalls);
                return new(null, null, otherPlan.Authority, SearchQueryEvaluation.Unknown("independent-check-completed"), "independent-check-completed");
            });
            await otherDelivery.Task.WaitAsync(TimeSpan.FromSeconds(10));
            store.Runtime.PollManualCandidateValidations(deliveryNow);
            Check(otherCalls == 1 && exactCalls == 1 && success.DeliveryAttempts == 1 && !store.Runtime.HasSearchInFlight &&
                store.DisplayResults.Single(result => result.Seed == Seed(2)).ValidationDisposition == SearchDisposition.Unknown,
                "storage failure does not block a different seed's calculation or cause the old Exact computation to run again");
            var onDisk = new SearchWorkspace(directory, false);
            Check(onDisk.DisplayResults.Single().EffectiveValidationDisposition == SearchDisposition.NotEvaluatedByPolicy &&
                onDisk.PendingHistories.SelectMany(SearchWorkspace.PendingForHistory).Single() == twice,
                "an unsuccessful save cannot erase the disk's old pending or pretend its unchecked row was validated");
        }
        store.Runtime.PollManualCandidateValidations(success.NextDeliveryAttemptAt);
        store = new(directory, false);
        Check(success.Accepted && otherDelivery is { Accepted: true } && exactCalls == 1 && otherCalls == 1 && store.PendingHistories.Count == 0 &&
            store.DisplayResults.Single(result => result.Seed == Seed(0)).EffectiveValidationDisposition == SearchDisposition.Match &&
            store.DisplayResults.Single(result => result.Seed == Seed(0)).SourceAssociation is { Kind: SeedQueryAssociationKind.VerifiedFromSearch },
            "after durable receipt the actual Match replaces its old row and only then resolves its original pending");

        // A second completed observation is written to the original history even
        // when another request is selected before the detached task finishes.
        AddUnverified(store, Seed(1));
        Check(store.TryManualValidationSource(Seed(1), out history, out row, out _), "second original source is available");
        string owner = history.Id;
        var secondPlan = ExactSearchExecutionRequestFactory.Compile(original.CompiledSearch, new(Seed(1), 1, 1, 1)).Plan!;
        var completed = new TaskCompletionSource<ProductionExactSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var detached = store.Runtime.TrackManualCandidateValidation(history, row, secondPlan,
            () => completed.Task.GetAwaiter().GetResult());
        int otherAscension = original.Ascension == SeedPredictionInputLimits.MaximumAscension ? original.Ascension - 1 : original.Ascension + 1;
        var otherDraft = draft with { Ascension = otherAscension };
        SearchPresetCompatibilityResolver.ValidateWorkbenchShape(otherDraft);
        Check(QueryHistoryIdentity.Key(otherDraft) != QueryHistoryIdentity.Key(draft),
            "the request selected during manual validation is a distinct legal authored query");
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("different-editor-query", otherDraft, context with { Ascension = otherAscension }));
        completed.SetResult(new(null, null, secondPlan.Authority, SearchQueryEvaluation.Unknown("actual-provider-unavailable"), "actual-provider-unavailable"));
        await detached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Runtime.PollManualCandidateValidations();
        store.FlushAll();
        store = new(directory, false);
        var originalHistory = store.Workspace.QueryHistory.SingleOrDefault(record => record.Id == owner);
        Check(store.QueryHistoryIssue.Length == 0 && originalHistory is not null,
            "both legal histories reopen without protective shape rejection: " + store.QueryHistoryIssue);
        Check(detached.Accepted && store.DisplayResults.Count == 0 &&
            originalHistory!.Results.Single(result => result.Seed == Seed(1)) is
                { ValidationDisposition: SearchDisposition.Unknown, ValidationIssue: "actual-provider-unavailable" },
            "a genuine Unknown observation is retained in its captured original history after the active request changes");
    }

    private static async Task CheckManualEventSourceRecapture(ExactSearchExecutionRequest original)
    {
        using var captured = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var targets = captured.RootElement.GetProperty("Evidence")[0].GetProperty("Targets")
            .Deserialize<MorphicGroveTarget[]>(ProductionSearchReplay.JsonOptions)!
            .Select((target, slot) => target with { InstanceId = "basic:" + slot }).ToArray();
        var time = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot At(DateTimeOffset capturedAt) => original.Authority
            .WithEffectAuthority(original.Authority.EffectAuthority! with { CapturedAtUtc = capturedAt })
            .WithWorldAuthority(original.Authority.WorldAuthority! with { CapturedAtUtc = capturedAt });
        var firstAuthority = At(time);
        var nextAuthority = At(time.AddMinutes(1));
        SearchQuery Query(bool aggregate, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
        {
            var scenario = new MorphicGroveScenario(authority, new("Authored initial Basics; event occurrence is a premise", true, false, null), targets);
            return aggregate ? SearchQuery.Empty with
            {
                EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3, new([new("EVENT", "MORPHIC_GROVE")], [], []))],
                TransformationAggregate = new(TransformationOpening.None, TransformationPickupOrder.Any, true, false, false,
                    TransformationAggregatePredicate.RareCountAtLeast, 1, []) { EventScenario = scenario }
            } : SearchQuery.Empty with
            {
                EventResultConditions = [new(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains,
                    targets[0].OrderedSourceCandidates![0].CardKey) { MorphicGroveScenario = scenario }]
            };
        }
        foreach (bool aggregate in new[] { false, true })
        {
            string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "manual-event-validation",
                (aggregate ? "T-" : "E-") + Guid.NewGuid().ToString("N"));
            var firstQuery = Query(aggregate, firstAuthority);
            var firstCompiled = SearchCompiler.Compile(firstQuery, original.CompiledSearch.Context with { Authority = firstAuthority });
            var nextCompiled = SearchCompiler.Compile(Query(aggregate, nextAuthority), original.CompiledSearch.Context with { Authority = nextAuthority });
            var firstExecution = ExactSearchExecutionRequestFactory.Compile(firstCompiled, new(Seed(0), 1, 1, 1));
            var nextExecution = ExactSearchExecutionRequestFactory.Compile(nextCompiled, new(Seed(0), 1, 1, 1));
            Check(firstExecution.Success && firstExecution.Plan is not null && nextExecution.Success && nextExecution.Plan is not null,
                "both original and recaptured E/T fixtures compile to admitted Exact requests");
            var firstPlan = firstExecution.Plan ?? throw new InvalidOperationException("Original E/T fixture: " + firstExecution.Issue);
            var nextPlan = nextExecution.Plan ?? throw new InvalidOperationException("Recaptured E/T fixture: " + nextExecution.Issue);
            Check(firstCompiled.SemanticFingerprint != nextCompiled.SemanticFingerprint &&
                firstPlan.SnapshotFingerprint != nextPlan.SnapshotFingerprint,
                "E/T recapture fixture genuinely changes the opaque semantic and attempt fingerprints through capture time");
            var store = new SearchWorkspace(directory, false);
            store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "manual-event-original-env" });
            var draft = new WorkbenchSearchDraft(original.CharacterKey, original.Ascension, firstQuery, AncientOptionConditionProfile.BroadDefault);
            var context = new SeedLibraryContext(original.Detection.NormalizedVersion, original.ProfileId, WorldGameMode.Singleplayer,
                original.Ascension, [new(0, original.CharacterKey,
                    JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState>("{}")!, "original-event-unlocks",
                    AncientOptionConditionProfile.BroadDefault, SearchQuery.Empty, null)]);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(firstCompiled.SemanticFingerprint, draft, context, skipExactValidation: true));
            store.AppendResult(new(Seed(0), original.ProfileId, original.CharacterKey, original.Ascension, firstPlan.SnapshotFingerprint,
                null!, null, firstAuthority, [], [], []) { ValidationDisposition = SearchDisposition.NotEvaluatedByPolicy }, firstCompiled.SemanticFingerprint);
            Check(store.TryManualValidationSource(Seed(0), out var history, out var row, out _), "E/T original source is available");
            var failure = new IOException("manual-event-original-provider-failure");
            var failed = store.Runtime.TrackManualCandidateValidation(history, row, firstPlan, () => throw failure);
            try { await failed.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException exception) when (ReferenceEquals(exception, failure)) { }
            store.Runtime.PollManualCandidateValidations();
            var pending = SearchWorkspace.PendingForHistory(history).Single();
            store = new(directory, false);
            Check(store.TryManualValidationSource(Seed(0), out history, out row, out _) &&
                history.ManualValidationSources.Count > 0 && SearchWorkspace.ProvesManualValidationSource(history, row, nextCompiled),
                "new E/T failure saves enough hash-only origin evidence to prove capture-time-only recapture after reopening");
            string savedFile = File.ReadAllText(Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json"));
            Check(!savedFile.Contains("OrderedSourceCandidates", StringComparison.Ordinal), "manual provenance stores no scenario pools or complete execution plans");
            var sourceIdentities = history.ManualValidationSources;
            history.ManualValidationSources = [];
            Check(!SearchWorkspace.ProvesManualValidationSource(history, row, nextCompiled),
                "old E/T histories without source metadata cannot guess equivalence from a changed opaque fingerprint");
            history.ManualValidationSources = sourceIdentities;
            // A new opaque audit hash alone is no longer a different input.
            // Change an actual world capability fact with that hash unchanged.
            var changedAuthority = nextAuthority.WithWorldAuthority(nextAuthority.WorldAuthority! with
                { CatalogOrderExact = !nextAuthority.WorldAuthority.CatalogOrderExact });
            var changed = SearchCompiler.Compile(Query(aggregate, changedAuthority), nextCompiled.Context with { Authority = changedAuthority });
            bool rejected = false;
            try
            {
                var changedPlan = ExactSearchExecutionRequestFactory.Compile(changed, new(Seed(0), 1, 1, 1)).Plan!;
                store.Runtime.TrackManualCandidateValidation(history, row, changedPlan, () => throw new Exception("Mismatch must not execute"));
            }
            catch (InvalidOperationException exception) when (exception.Message == "ManualValidationOriginalAuthorityMismatch") { rejected = true; }
            Check(rejected && SearchWorkspace.PendingForHistory(history).Single() == pending,
                "a real authority change is rejected while retaining the original E/T failure and obligation");
            var changedPoolQuery = Query(aggregate, nextAuthority);
            var originalScenario = aggregate ? changedPoolQuery.TransformationAggregate!.EventScenario! : changedPoolQuery.EventResultConditions.Single().MorphicGroveScenario!;
            var changedPool = new MorphicGroveScenario(nextAuthority, originalScenario.Premises, originalScenario.Targets.Select(target =>
                target with { OrderedSourceCandidates = target.OrderedSourceCandidates!.Reverse().ToArray() }).ToArray());
            changedPoolQuery = aggregate ? changedPoolQuery with { TransformationAggregate = changedPoolQuery.TransformationAggregate! with { EventScenario = changedPool } } :
                changedPoolQuery with { EventResultConditions = [changedPoolQuery.EventResultConditions.Single() with { MorphicGroveScenario = changedPool }] };
            Check(!SearchWorkspace.ProvesManualValidationSource(history, row, SearchCompiler.Compile(changedPoolQuery, nextCompiled.Context)),
                "source proof also rejects changed scenario pool order when all scalar authority fingerprints remain the same");
            var next = store.Runtime.TrackManualCandidateValidation(history, row, nextPlan, () =>
                new(null, null, nextAuthority, SearchQueryEvaluation.NoMatch("actual-event-model-mismatch"), "actual-event-model-mismatch"));
            await next.Task.WaitAsync(TimeSpan.FromSeconds(10));
            store.Runtime.PollManualCandidateValidations();
            store = new(directory, false);
            var sourceContinuation = next.SourceContinuation ?? throw new InvalidOperationException("E/T retry lost its explicit original continuation");
            Check(next.Accepted && next.Plan.SnapshotFingerprint == nextPlan.SnapshotFingerprint &&
                sourceContinuation.SemanticFingerprint == firstCompiled.SemanticFingerprint &&
                sourceContinuation.SnapshotFingerprint == pending.SnapshotFingerprint && store.PendingHistories.Count == 0 &&
                store.DisplayResults.Single() is { ValidationDisposition: SearchDisposition.NoMatch, ValidationIssue: "actual-event-model-mismatch" } observation &&
                observation.QueryFingerprint == nextCompiled.SemanticFingerprint,
                "equivalent E/T recapture retries with its genuine new identities, records the actual NoMatch and resolves only the proven origin obligation");
        }
    }

    private static ProductionSearchSession Start(ExactSearchExecutionRequest plan,
        Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult> evaluator,
        SearchContinuationSnapshot? continuation = null, IFamilyInvocation? family = null, int window = 4,
        Func<int, CancellationToken, ValueTask>? beforePublish = null,
        IRuntimePredictionDiagnosticSink? sink = null, string traceSeed = "",
        long payloadLimit = ProductionSearchSession.DefaultPendingPayloadLimitBytes,
        Func<SearchPendingCandidate, long>? measurePayload = null, Action? receiptWaitStarted = null)
    {
        var execution = FamilyPlanner.InOrder(family is null ? [] : [family]) with
        {
            AllConditionsUnfiltered = family is null,
            FinalValidationSkipped = plan.RunOptions.SkipExactValidation
        };
        return new(plan, execution, FamilySearchEtaProjectorV1.Project(plan, execution), window, 16,
            sink, traceSeed, exactEvaluator: evaluator, beforeCandidatePublish: beforePublish, continuation: continuation,
            pendingPayloadLimitBytes: payloadLimit, measurePendingPayload: measurePayload,
            recoveryReceiptWaitStarted: receiptWaitStarted);
    }

    private static Task Finish(ProductionSearchSession session) => session.Completion.WaitAsync(TimeSpan.FromSeconds(15));
    private static SearchContinuationSnapshot Snapshot(ProductionSearchSession session) =>
        session.CaptureContinuation() ?? throw new InvalidOperationException("Missing initialized continuation");
    private static SearchCandidate[] Drain(ProductionSearchSession session)
    {
        var result = new List<SearchCandidate>();
        while (session.TryReadCandidate(out var candidate)) result.Add(candidate!);
        return result.ToArray();
    }
    private static async Task<SearchCandidate> ReadOne(ProductionSearchSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (session.TryReadCandidate(out var candidate)) return candidate!;
            if (session.Completion.IsCompleted) throw new InvalidOperationException("Session ended without the required first publication");
            await Task.Delay(5, timeout.Token);
        }
    }
    private static ExactSearchExecutionRequest Recompile(ExactSearchExecutionRequest plan, SearchRunOptions options)
    {
        var result = ExactSearchExecutionRequestFactory.Compile(plan.CompiledSearch, options);
        Check(result.Success && result.Plan is not null, "actual execution compiler accepts resumed options: " + result.Issue);
        return result.Plan!;
    }

    private static ExactSearchExecutionRequest ResumePlan(ExactSearchExecutionRequest plan, ulong start) =>
        Recompile(plan, plan.RunOptions with { StartSeed = Seed(start), ScanCount = checked(4 - (long)start) });

    private static async Task CheckExplicitOriginPersistence(ExactSearchExecutionRequest original)
    {
        foreach (string mode in new[] { "manual-forward", "manual-backward", "random", "manual-restart" })
        {
            string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "explicit-origin", mode, Guid.NewGuid().ToString("N"));
            var environment = new SearchEnvironmentSignature { CaptureComplete = true, Fingerprint = "origin-environment",
                GameVersionIdentity = original.Authority.GameVersion };
            var draft = new WorkbenchSearchDraft(original.CharacterKey, original.Ascension,
                original.CompiledSearch.NormalizedQuery, AncientOptionConditionProfile.BroadDefault);
            var store = new SearchWorkspace(directory, false);
            store.EnsureEnvironment(environment);
            store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(original.CompiledSearch.SemanticFingerprint, draft));
            await using (var first = Start(original, (request, input) => Ordinal(input.CanonicalSeed) switch
                { 0 => throw new IOException("OriginalExplicitOriginFailure"), 3 => Reject(request), _ => Match(request, input) }))
            {
                await Finish(first);
                var accepted = Drain(first).Single(candidate => candidate.Seed == Seed(2));
                store.AppendResult(accepted, original.CompiledSearch.SemanticFingerprint);
                first.AcknowledgeCandidate(accepted.Seed);
                Checkpoint(store, first, true); store.FlushAll();
            }
            string selected;
            if (mode == "random") selected = store.RandomizePersistentOrigin();
            else
            {
                selected = Seed(mode == "manual-backward" ? 0UL : 10UL);
                Check(store.TrySetPersistentOrigin(selected, out _, out _), "Settings manual origin accepted");
            }
            ulong origin = Ordinal(selected);
            if (mode is "manual-restart" or "random") store = new(directory, false);
            int scanCount = (int)Math.Min(3UL, Beta110SeedCodec.SpaceSize - origin);
            var start = store.ResolveSearchStart(draft, Recompile(original, new(store.CurrentNextCursorSeed, scanCount, 20, 1)));
            Check(start.Plan.CanonicalStartSeed == selected && start.ExplicitOriginChangeId.Length > 0 &&
                start.Continuation is { NextOrdinal: 1, PendingCandidates.Count: 1 } &&
                start.Continuation.PublishedOrdinals.SequenceEqual(new ulong[] { 2 }) &&
                start.Continuation.UnreceivedOrdinals!.SequenceEqual(new ulong[] { 1 }),
                mode + ": applied origin wins while complete failure, ACK and unreceived ownership remains");
            Check(store.ResolveSearchStart(draft, start.Plan).ExplicitOriginChangeId == start.ExplicitOriginChangeId,
                "Resolving/compiling a start does not consume an intent before a session starts");
            var calls = new ConcurrentDictionary<ulong, int>();
            ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest request, TrustedRootHashInput input)
            {
                ulong ordinal = Ordinal(input.CanonicalSeed);
                calls.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
                return ordinal <= 2 ? Match(request, input) : Reject(request);
            }
            var receiving = store.PrepareSearchStart(draft, start.Plan, null);
            await using (var session = Start(start.Plan, Evaluate, start.Continuation))
            {
                store.BindPreparedResultWorkspace(receiving);
                store.FlushAll();
                var immediatelyReopened = new SearchWorkspace(directory, false);
                var entrance = immediatelyReopened.ResolveSearchStart(draft, start.Plan);
                Check(entrance.ExplicitOriginChangeId.Length == 0 && entrance.Plan.CanonicalStartSeed == selected &&
                    entrance.Continuation!.UnreceivedOrdinals!.SequenceEqual(new ulong[] { 1 }),
                    "Restart immediately after successful binding retains new entrance and prior undelivered results");
                await Finish(session);
                var outputs = Drain(session);
                Check(outputs.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 0, 1 }) &&
                    calls.GetValueOrDefault(0UL) == 1 && calls.GetValueOrDefault(1UL) == 1 && calls.GetValueOrDefault(2UL) == 0,
                    mode + ": failed/unreceived results recover once and acknowledged output is not republished");
                Check(calls.Keys.Where(o => o > 2).All(o => o >= origin && o < origin + (ulong)scanCount) &&
                    (origin <= 2 || calls.ContainsKey(origin)), "Ordinary computation actually starts in the requested range");
                var checkpoint = Snapshot(session);
                Check(checkpoint.UnreceivedOrdinals!.SequenceEqual(new ulong[] { 0, 1 }) &&
                    (origin <= 2 || checkpoint.NextOrdinal == origin + (ulong)scanCount),
                    "Unreceived recovered results remain explicit without rewinding the requested ordinary frontier");
                Checkpoint(store, session, true); store.FlushAll();
            }
            store = new(directory, false);
            var resumed = store.ResolveSearchStart(draft, Recompile(original, new(store.CurrentNextCursorSeed, 1, 20, 1)));
            Check(resumed.ExplicitOriginChangeId.Length == 0 && resumed.Continuation!.UnreceivedOrdinals!.Count == 2,
                "Successful start consumes intent atomically with the receiving workspace and preserves unACKed deliveries");
            calls.Clear();
            receiving = store.PrepareSearchStart(draft, resumed.Plan, null);
            await using (var session = Start(resumed.Plan, Evaluate, resumed.Continuation))
            {
                store.BindPreparedResultWorkspace(receiving);
                await Finish(session);
                foreach (var output in Drain(session))
                { store.AppendResult(output, resumed.Plan.CompiledSearch.SemanticFingerprint); session.AcknowledgeCandidate(output.Seed); }
                Checkpoint(store, session, true); store.FlushAll();
            }
            store = new(directory, false);
            var own = store.ContinuationFor(draft, resumed.Plan)!;
            Check(own.PendingCandidates.Count == 0 && own.UnreceivedOrdinals!.Count == 0 &&
                store.DisplayResults.Select(r => r.Seed).Order().SequenceEqual(new[] { Seed(0), Seed(1), Seed(2) }),
                "Repeated restart/ACK resolves the original obligations without losing stored results");
            store.ObserveSafeNextCursor(999);
            var restored = store.ResolveSearchStart(draft, Recompile(original, new(store.CurrentNextCursorSeed, 1, 20, 1)));
            Check(restored.Plan.CanonicalStartSeed == Seed(own.NextOrdinal),
                "Without a fresh user request, another query advancing the global cursor cannot steal this query's frontier");
        }
        var delivery = new SearchContinuationSnapshot(original.CompiledSearch.SemanticFingerprint, original.SnapshotFingerprint,
            1, [], []) { ContinuationFingerprint = SearchContinuationIdentity.For(original), UnreceivedOrdinals = [1] };
        var faultPlan = Recompile(original, new(Seed(10), 1, 20, 1));
        await using var fault = Start(faultPlan, (request, input) => Ordinal(input.CanonicalSeed) == 1
            ? throw new IOException("DeliveryRecoveryOriginalFailure") : Reject(request), delivery);
        await Finish(fault);
        Check(Snapshot(fault) is { NextOrdinal: 11, PendingCandidates.Count: 1 } failed &&
            failed.PendingCandidates[0].Ordinal == 1 && failed.PendingCandidates[0].ExceptionText.Contains("DeliveryRecoveryOriginalFailure") &&
            fault.GetProgress().MatchCount == 0,
            "A delivery recovery exception remains pending with its actual cause and no target credit");
    }

    private static async Task CheckRecompiledPersistenceResume(ExactSearchExecutionRequest original)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "compiled-continuation", Guid.NewGuid().ToString("N"));
        var environment = new SearchEnvironmentSignature { CaptureComplete = true, Fingerprint = "compiled-environment",
            GameVersionIdentity = original.Authority.GameVersion };
        var draft = new WorkbenchSearchDraft(original.CharacterKey, original.Ascension,
            original.CompiledSearch.NormalizedQuery, AncientOptionConditionProfile.BroadDefault);
        var store = new SearchWorkspace(directory, false);
        store.EnsureEnvironment(environment);
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(original.CompiledSearch.SemanticFingerprint, draft, null));
        SearchPendingCandidate firstFailure;
        await using (var first = Start(original, (request, input) => Ordinal(input.CanonicalSeed) switch
        { 0 => throw new IOException("CompiledResumeOriginalFailure"), 3 => Reject(request), _ => Match(request, input) }))
        {
            await Finish(first);
            var results = Drain(first);
            var acceptedTail = results.Single(candidate => candidate.Seed == Seed(2));
            store.AppendResult(acceptedTail, original.CompiledSearch.SemanticFingerprint);
            first.AcknowledgeCandidate(acceptedTail.Seed);
            Checkpoint(store, first, true);
            firstFailure = SearchWorkspace.PendingForHistory(store.Workspace.QueryHistory.Single()).Single();
            store.FlushAll();
        }
        store = new(directory, false);
        Check(store.CurrentNextCursorSeed == Seed(1), "actual persisted cursor stops at the unACKed ordinary result");
        var resumed = Recompile(original, new(store.CurrentNextCursorSeed, 5, 20, 2));
        Check(resumed.SnapshotFingerprint != original.SnapshotFingerprint &&
            SearchContinuationIdentity.For(resumed) == SearchContinuationIdentity.For(original),
            "start/count/target/workers change attempt identity, not stable request identity");
        var continuation = store.ContinuationFor(draft, resumed);
        Check(continuation is not null && continuation.PendingCandidates.Single() == firstFailure &&
            continuation.PublishedOrdinals.SequenceEqual(new ulong[] { 2 }), "restart plus real Compile finds pending and ACK tail");

        var changedQuery = original.CompiledSearch.NormalizedQuery with
        { LegacyNeow = original.CompiledSearch.NormalizedQuery.LegacyNeow with
            { NeowRelics = new ModelKeySetFilter([BaseGameModelKeys.Relics.Kaleidoscope], [], []) } };
        var changedCompiled = SearchCompiler.Compile(changedQuery, original.CompiledSearch.Context);
        var changed = ExactSearchExecutionRequestFactory.Compile(changedCompiled, resumed.RunOptions).Plan!;
        Check(changed is not null && store.ContinuationFor(draft, changed) is null,
            "another actual request cannot reuse pending/ACK even under the same history lookup");
        store.EnsureEnvironment(new() { CaptureComplete = true, Fingerprint = "another-content-environment" });
        Check(store.ContinuationFor(draft, resumed) is null && store.PendingHistories.Count == 1,
            "a genuinely different captured environment preserves but cannot reuse the old pending");
        store.EnsureEnvironment(environment);

        var calls = new ConcurrentDictionary<ulong, int>();
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(resumed.CompiledSearch.SemanticFingerprint, draft, null));
        await using (var retry = Start(resumed, (request, input) =>
        {
            ulong ordinal = Ordinal(input.CanonicalSeed);
            calls.AddOrUpdate(ordinal, 1, (_, count) => count + 1);
            return ordinal <= 2 ? Match(request, input) : Reject(request);
        }, continuation: continuation))
        {
            await Finish(retry);
            var outputs = Drain(retry);
            Check(outputs.Select(c => Ordinal(c.Seed)).SequenceEqual(new ulong[] { 0, 1 }) &&
                calls.GetValueOrDefault(0UL) == 1 && calls.GetValueOrDefault(1UL) == 1 && calls.GetValueOrDefault(2UL) == 0,
                "real recompiled resume retries failure once, delivers the unACKed result and never repeats the acknowledged tail");
            foreach (var output in outputs)
            { store.AppendResult(output, resumed.CompiledSearch.SemanticFingerprint); retry.AcknowledgeCandidate(output.Seed); }
            Checkpoint(store, retry, true);
            store.FlushAll();
        }
        store = new(directory, false);
        Check(store.PendingHistories.Count == 0 && store.DisplayResults.Select(r => r.Seed).Order().SequenceEqual(
            new[] { Seed(0), Seed(1), Seed(2) }), "results and resolved pending/ACK survive the next restart together");
        var finalPlan = Recompile(original, new(store.CurrentNextCursorSeed, 1, 20, 1));
        Check(store.ContinuationFor(draft, finalPlan) is { PendingCandidates.Count: 0 },
            "next actual compilation finds the updated continuation without retaining the resolved older snapshot");
    }
    private static string Seed(ulong ordinal) => Beta110SeedCodec.FormatOrdinal(ordinal);
    private static ulong Ordinal(string seed)
    {
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, seed, out var ordinal, out _, out var issue))
            throw new InvalidOperationException("Fixture seed identity: " + issue);
        return ordinal;
    }
    private static ProductionExactSearchResult Reject(ExactSearchExecutionRequest request) =>
        new(null, null, request.Authority, SearchQueryEvaluation.NoMatch("ExplicitFixtureMismatch"), "ExplicitFixtureMismatch");
    private static ProductionExactSearchResult Match(ExactSearchExecutionRequest request, TrustedRootHashInput input)
    {
        if (!SeedPredictionRequest.TryCreateFromRootHash(input, CharacterIdentity.FromKey(request.CharacterKey), request.Ascension,
            request.Authority.PlayersCount, request.Authority.PlayerSlotIndex, request.Authority,
            request.Evaluation.AncientOptionConditions, SeedPredictionDomainSelection.None,
            SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, true, out var predictionRequest, out var issue))
            throw new InvalidOperationException("FailureContinuationFixtureRequest:" + issue);
        var document = new SeedPredictionDocument
        {
            Context = SeedPredictionDocument.CreateContext(predictionRequest!, request.Detection.DisplayVersion, request.ProfileId),
            PredictorId = "FailureContinuationFixture", OriginalSeed = input.CanonicalSeed, CanonicalSeed = input.CanonicalSeed,
            Sections = [], Warnings = [], Diagnostics = [], OverallStatus = SeedPredictionOverallStatus.Completed
        };
        return new(predictionRequest, document, request.Authority,
            SearchQueryEvaluation.Match([new("FixtureEvidence")], ["fixture:" + input.CanonicalSeed]), "");
    }

    private sealed class ThrowingTraceSink : IRuntimePredictionDiagnosticSink
    {
        internal int Calls;
        public bool Enabled => true;
        public RuntimePredictionDiagnosticLevel Verbosity => RuntimePredictionDiagnosticLevel.Trace;
        public string LogDirectory => "";
        public string LatestLogPath => "";
        public string LastFailureCode => "";
        public void Configure(bool enabled, RuntimePredictionDiagnosticLevel verbosity) { }
        public bool TryWrite(RuntimePredictionDiagnosticEvent diagnosticEvent, bool force = false)
        {
            Interlocked.Increment(ref Calls);
            throw new IOException("InjectedTraceSinkFailure");
        }
    }

    private sealed class FixtureFamily(bool cleanupFails = false, Task? untrustedSecondBatch = null) : IFamilyInvocation
    {
        private int _calls;
        public string FamilyId => "FailureContinuationFixture";
        public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, 1, [], "fixture");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Resolved(FamilyId, 1, "fixture");
        public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId, FamilyId, "fixture", 1, "fixture", usesGpu: false);
        public FamilyPerformanceObservation CapturePerformanceObservation() =>
            new(ConditionPerformance, "", 0, 0, 0, _calls, 0, 0, 0, 0, false, "FixtureNoCalibration");
        public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,
            FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 2 && untrustedSecondBatch is not null)
            {
                await untrustedSecondBatch.WaitAsync(token);
                return FamilyCandidateSet.Dense(new SearchBatch(window.Batch.BatchBase + 1, window.Batch.BatchCandidateCount));
            }
            return input;
        }
        public ValueTask DisposeAsync(FamilyExecutionContext context) => cleanupFails
            ? ValueTask.FromException(new IOException("InjectedFamilyCleanupFailure")) : ValueTask.CompletedTask;
    }
}
