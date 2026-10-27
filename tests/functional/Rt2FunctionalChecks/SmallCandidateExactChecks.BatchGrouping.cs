using System.Collections.Concurrent;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class SmallCandidateExactChecks
{
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(15);
    private static async Task CheckDenseBatchGroupingAsync(ExactSearchExecutionRequest template)
    {
        foreach (int count in new[] { 48, 49, 56, 57, 73, 97 })
        {
            var request = PairRequest(template, count);
            var single = await RunPairComparisonAsync(request, grouped: false);
            var grouped = await RunPairComparisonAsync(request, grouped: true);
            string[] expected = Enumerable.Range(0, (count + 15) / 16)
                .SelectMany(window => Enumerable.Range(window * 16, Math.Min(9, count - window * 16)))
                .Select(ordinal => Beta110SeedCodec.FormatOrdinal((ulong)ordinal)).Order(StringComparer.Ordinal).ToArray();
            Check(single.Seeds.SequenceEqual(expected) && grouped.Seeds.SequenceEqual(expected),
                $"real Session K1/K2 preserve every candidate across odd and 1/8/9 tails;count={count}");
            Check(single.Exact == expected.Length && grouped.Exact == expected.Length,
                "K1/K2 execute complete-request Production Exact once per survivor");
            Check(single.Cursor == (ulong)count && grouped.Cursor == (ulong)count && single.Scanned == count && grouped.Scanned == count,
                "K1/K2 ACK and scanned frontiers cover identical original root ranges");
            Check(single.PairCalls == 0 && grouped.PairCalls > 0 && !single.GpuOwnerObserved && !grouped.GpuOwnerObserved,
                "explicit opt-in groups after the initial single root without acquiring a GPU owner");
            Check(single.TailCalls == count / 16 + (count % 16 > 8 ? 1 : 0) && grouped.TailCalls == single.TailCalls,
                "prepared prefixes still traverse the original Linear tail and retain tiny Exact handoff");
            if (count == 73)
            {
                var preferredSingle = await RunPairComparisonAsync(request, grouped: true, preferGrouping: false);
                Check(preferredSingle.PairCalls == 0 && preferredSingle.Seeds.SequenceEqual(single.Seeds) &&
                    preferredSingle.Exact == single.Exact && preferredSingle.Cursor == single.Cursor,
                    "supported grouping can prefer ordinary execution without changing Exact, candidates or cursor");
            }
        }
        await CheckPairHandlesAsync(template);
        await CheckPairCpuIsolationAsync(PairRequest(template, 65));
        await CheckPairTargetAndCancelAsync(PairRequest(template, 64));
        await CheckPairSharedFailureAsync(PairRequest(template, 64));
        foreach (string phase in new[] { "copy", "queue", "completion" })
            await CheckPairDeferredFaultAsync(PairRequest(template, 64), phase, targetWins: false);
        await CheckPairDeferredFaultAsync(PairRequest(template, 64, target: 1), "completion-target", targetWins: true);
        foreach (PairCorruption corruption in new[] { PairCorruption.FirstOwner, PairCorruption.SecondBatch })
        {
            var fixture = new DensePairFixture(grouped: true, population: 1, emptyInitial: true) { Corruption = corruption };
            await using var session = PairSession(PairRequest(template, 64), [fixture]);
            await session.Completion.WaitAsync(PairTimeout);
            Check(session.GetProgress().State == SearchRunState.Faulted && Drain(session).Length == 0 &&
                session.CaptureContinuation()!.NextOrdinal == 16,
                "actual Session rejects malformed prepared owner/batch before any grouped frontier advances: " + corruption);
            Check(session.GetProgress().FailureCode.Contains(corruption == PairCorruption.FirstOwner
                ? "PreparedFamilyBatch.OwnerOrBatchMismatch" : "RootPair.PreparedBatchIdentityMismatch", StringComparison.Ordinal),
                "prepared identity failures retain their actual producer/consumer cause");
        }
    }
    private sealed record PairRun(string[] Seeds, int Exact, ulong Cursor, long Scanned, int PairCalls, int TailCalls, bool GpuOwnerObserved);
    private static async Task<PairRun> RunPairComparisonAsync(ExactSearchExecutionRequest request, bool grouped, bool preferGrouping = true)
    {
        var prefix = new DensePairFixture(grouped, 9) { PreferGrouping = preferGrouping };
        var tail = new FixtureFamily("pair-tail", 9);
        var visits = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        await using var session = PairSession(request, [prefix, tail], evaluator: (plan, input) =>
        {
            visits.AddOrUpdate(input.CanonicalSeed, 1, (_, count) => count + 1);
            return ProductionExactSearchEvaluator.Evaluate(plan, input);
        });
        await session.Completion.WaitAsync(PairTimeout);
        var candidates = Drain(session);
        Check(session.GetProgress().State == SearchRunState.Completed && candidates.All(candidate => !candidate.IsUnverified) &&
            visits.Values.All(count => count == 1), "pair comparison delivers confirmed real Exact results without duplicate checks");
        foreach (var candidate in candidates) session.AcknowledgeCandidate(candidate.Seed);
        return new(candidates.Select(candidate => candidate.Seed).Order(StringComparer.Ordinal).ToArray(), visits.Count,
            session.CaptureContinuation()!.NextOrdinal, session.GetProgress().ScannedCount, prefix.PairCalls, tail.Calls, prefix.GpuOwnerObserved);
    }
    private static async Task CheckPairHandlesAsync(ExactSearchExecutionRequest request)
    {
        var owner = new DensePairFixture(true, 9);
        var other = new DensePairFixture(true, 9);
        var batch = new SearchBatch(16, 16);
        var output = owner.Select(batch);
        var ready = PreparedFamilyBatch.Ready(owner, output);
        var input = FamilyCandidateSet.Dense(batch);
        bool wrongOwner = false;
        try { await ready.InvokeAsync(other, new(batch, request), input, default); }
        catch (InvalidDataException error) when (error.Message == "PreparedFamilyBatch.OwnerOrBatchMismatch") { wrongOwner = true; }
        Check(wrongOwner, "prepared owner validation rejects another family without consuming the handle");
        Check(ReferenceEquals(await ready.InvokeAsync(owner, new(batch, request), input, default), output),
            "correct owner consumes its exact immutable prepared result");
        bool reused = false;
        try { await ready.InvokeAsync(owner, new(batch, request), input, default); }
        catch (InvalidOperationException error) when (error.Message == "PreparedFamilyBatch.AlreadyConsumed") { reused = true; }
        Check(reused, "prepared handles cannot be consumed twice");
        foreach (bool wrongBatch in new[] { false, true })
        {
            var handle = PreparedFamilyBatch.Ready(owner, output);
            var observation = new FamilyObservationWindow(wrongBatch ? new SearchBatch(32, 16) : batch, request);
            bool invalid = false;
            try { await handle.InvokeAsync(owner, observation, wrongBatch ? input : output, default); }
            catch (InvalidDataException error) when (error.Message == "PreparedFamilyBatch.OwnerOrBatchMismatch") { invalid = true; }
            Check(invalid, "prepared handle rejects mismatched observation or Compact input");
        }
        var cancelled = PreparedFamilyBatch.Ready(owner, output);
        bool stopped = false;
        try { await cancelled.InvokeAsync(owner, new(batch, request), input, new CancellationToken(true)); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped && ReferenceEquals(await cancelled.InvokeAsync(owner, new(batch, request), input, default), output),
            "cancellation before consumption preserves the ready handle");
    }
    private static async Task CheckPairCpuIsolationAsync(ExactSearchExecutionRequest request)
    {
        var prefix = new DensePairFixture(true, 9);
        int matcherCalls = 0;
        var cause = new IOException("InjectedPairPureCpuMatcher");
        var cpu = new FamilyCpuExecution(request, new FixtureFamily("pair-cpu-isolation", 0), "PairCpuIsolationFixture", _ =>
        { Interlocked.Increment(ref matcherCalls); throw cause; }, 2, _ => null);
        var tail = new FixtureFamily("must-not-run-after-isolation", 0);
        await using var session = PairSession(request, [prefix, cpu, tail]);
        await session.Completion.WaitAsync(PairTimeout);
        var candidates = Drain(session);
        Check(prefix.PairCalls > 0 && matcherCalls > 0 && tail.Calls == 0 && session.GetProgress().State == SearchRunState.Completed,
            "prepared prefixes preserve actual pure CPU matcher isolation and skip dependent Linear tails");
        Check(candidates.Length == 37 && candidates.All(candidate => !candidate.IsUnverified),
            "isolated CPU tails restore all prefix survivors to original complete-request Exact");
        foreach (var candidate in candidates) session.AcknowledgeCandidate(candidate.Seed);
        Check(session.CaptureContinuation()!.NextOrdinal == 65, "CPU isolation inside grouped flow retains ACK frontier ownership");
    }
    private static async Task CheckPairTargetAndCancelAsync(ExactSearchExecutionRequest request)
    {
        var targetPrefix = new DensePairFixture(true, 1);
        await using (var target = PairSession(PairRequest(request, 64, target: 2), [targetPrefix]))
        {
            await target.Completion.WaitAsync(PairTimeout);
            var candidates = Drain(target);
            Check(targetPrefix.PairCalls > 0 && candidates.Length == 2 && target.GetProgress().State == SearchRunState.Completed,
                "target completion keeps its original two-result limit across prepared roots");
            Check(candidates.Select(candidate => candidate.Seed).Order(StringComparer.Ordinal).SequenceEqual(
                new ulong[] { 0, 16 }.Select(Beta110SeedCodec.FormatOrdinal).Order(StringComparer.Ordinal)),
                "lookahead cannot publish a later prepared root after the target");
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken paused = default;
        SearchContinuationSnapshot continuation;
        await using (var session = PairSession(request, [new DensePairFixture(true, 1)], beforePublish: async (number, token) =>
        {
            if (number != 2) return;
            paused = token; entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }))
        {
            await entered.Task.WaitAsync(PairTimeout);
            var delivered = Drain(session);
            Check(delivered.Length == 1 && delivered[0].Seed == Beta110SeedCodec.FormatOrdinal(0),
                "initial single root is delivered before cancellation pauses the grouped root");
            session.AcknowledgeCandidate(delivered[0].Seed);
            session.Cancel();
            await session.Completion.WaitAsync(PairTimeout);
            continuation = session.CaptureContinuation()!;
            Check(paused.IsCancellationRequested && session.GetProgress().State == SearchRunState.Cancelled && continuation.NextOrdinal == 16,
                "explicit user cancellation still cancels active Exact and retains the unreceived grouped frontier");
        }
        await using var resumed = PairSession(PairResumeRequest(request, continuation.NextOrdinal),
            [new DensePairFixture(true, 1)], continuation: continuation);
        await resumed.Completion.WaitAsync(PairTimeout);
        var remaining = Drain(resumed);
        Check(resumed.GetProgress().State == SearchRunState.Completed && remaining.Select(candidate => candidate.Seed).Order(StringComparer.Ordinal)
            .SequenceEqual(new ulong[] { 16, 32, 48 }.Select(Beta110SeedCodec.FormatOrdinal).Order(StringComparer.Ordinal)),
            "resume retains original candidate identities and delivers every unreceived root once");
        foreach (var candidate in remaining) resumed.AcknowledgeCandidate(candidate.Seed);
        Check(resumed.CaptureContinuation()!.NextOrdinal == 64, "resumed pair ACK frontier reaches the original full range");
    }
    private static async Task CheckPairSharedFailureAsync(ExactSearchExecutionRequest request)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken exactToken = default;
        var original = new InvalidOperationException("InjectedSharedDeviceBoundaryFailure");
        var fixture = new DensePairFixture(true, 1)
        {
            GroupFailure = original,
            BeforeGroupFailure = () => entered.Task.WaitAsync(PairTimeout).GetAwaiter().GetResult()
        };
        await using var session = PairSession(request, [fixture], beforePublish: async (_, token) =>
        {
            exactToken = token; entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        await session.Completion.WaitAsync(PairTimeout);
        Check(exactToken.IsCancellationRequested && session.GetProgress().State == SearchRunState.Faulted &&
            session.GetProgress().FailureCode.Contains(original.Message, StringComparison.Ordinal),
            "shared/device-wide group errors retain original immediate cancellation and fault behavior");
        Check(Drain(session).Length == 0 && session.CaptureContinuation()!.NextOrdinal == 0,
            "untrusted shared failure cannot advance the paused unreceived frontier");
    }
    private static async Task CheckPairDeferredFaultAsync(ExactSearchExecutionRequest request, string phase, bool targetWins)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new InvalidOperationException("OriginalGpuCause_" + phase);
        Exception original = phase == "copy" ? new IOException("InjectedIsolatedRight_" + phase, cause) :
            phase == "queue" ? new InvalidOperationException("InjectedIsolatedRight_" + phase, cause) :
            new InvalidDataException("InjectedIsolatedRight_" + phase, cause);
        CancellationToken exactToken = default;
        var prefix = new DensePairFixture(true, 1, emptyInitial: true)
        {
            RightFailure = original,
            BeforeRightFailure = () =>
            {
                entered.Task.WaitAsync(PairTimeout).GetAwaiter().GetResult();
                failedCallback.TrySetResult();
            }
        };
        await using var session = PairSession(request, [prefix], beforePublish: async (_, token) =>
        {
            exactToken = token; entered.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        try
        {
            await entered.Task.WaitAsync(PairTimeout);
            await failedCallback.Task.WaitAsync(PairTimeout);
            // The first grouped terminal has been dequeued and is paused in Exact.
            // Await the actual channel completion to prove the producer has handled
            // the isolated later failure, without relying on sleeps or scheduling.
            Exception? completionFailure = null;
            try { await TerminalReaderCompletion(session).WaitAsync(PairTimeout); }
            catch (Exception error) { completionFailure = error; }
            Check(ReferenceEquals(completionFailure, original), "real terminal completion preserves original isolated " + phase + " exception");
            Check(!exactToken.IsCancellationRequested && !session.Completion.IsCompleted && prefix.FailedConsumed,
                "isolated later " + phase + " failure does not cancel or discard slow trusted Exact");
            Check(Drain(session).Length == 0 && session.CaptureContinuation()!.NextOrdinal == 16,
                "paused publication does not advance the first pair member or the failed right root");
            release.TrySetResult();
            await session.Completion.WaitAsync(PairTimeout);
            var candidates = Drain(session);
            Check(candidates.Length == 1 && candidates[0].Seed == Beta110SeedCodec.FormatOrdinal(16) && !candidates[0].IsUnverified,
                "trusted first member really delivers after isolated later " + phase + " failure");
            session.AcknowledgeCandidate(candidates[0].Seed);
            Check(session.CaptureContinuation()!.NextOrdinal == 32 && session.GetProgress().ScannedCount == 32,
                "ACK commits only trusted first pair range; failed second range remains unadvanced");
            Check(session.GetProgress().State == (targetWins ? SearchRunState.Completed : SearchRunState.Faulted),
                "target completion and original error-after-drain retain their distinct state meanings");
            if (!targetWins)
                Check(session.GetProgress().FailureCode.Contains(original.Message, StringComparison.Ordinal) &&
                    !session.GetProgress().FailureCode.Contains("DeferredRootBatchFailure", StringComparison.Ordinal),
                    "faulted Session reports original cause after trusted Exact drains");
            if (phase == "copy")
            {
                var continuation = session.CaptureContinuation()!;
                await using var resumed = PairSession(PairResumeRequest(request, continuation.NextOrdinal),
                    [new DensePairFixture(true, 1, emptyInitial: true)], continuation: continuation);
                await resumed.Completion.WaitAsync(PairTimeout);
                var remaining = Drain(resumed);
                Check(remaining.Select(candidate => candidate.Seed).Order(StringComparer.Ordinal).SequenceEqual(
                    new ulong[] { 32, 48 }.Select(Beta110SeedCodec.FormatOrdinal).Order(StringComparer.Ordinal)),
                    "resume retries the failed right root while preserving ACK of the trusted left delivery");
                foreach (var candidate in remaining) resumed.AcknowledgeCandidate(candidate.Seed);
                Check(resumed.CaptureContinuation()!.NextOrdinal == 64 && resumed.GetProgress().State == SearchRunState.Completed,
                    "resumed isolated-member failure reaches the full original frontier");
            }
        }
        finally { release.TrySetResult(); }
    }
    private static Task TerminalReaderCompletion(ProductionSearchSession session)
    {
        object channel = typeof(ProductionSearchSession).GetField("_terminalHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        object reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        return (Task)reader.GetType().GetProperty("Completion")!.GetValue(reader)!;
    }
    private static ExactSearchExecutionRequest PairRequest(ExactSearchExecutionRequest template, int count, int target = 100) =>
        ExactSearchExecutionRequestFactory.Compile(template.CompiledSearch,
            template.RunOptions with { ScanCount = count, TargetMatchCount = target }).Plan
        ?? throw new InvalidOperationException("DensePairFixture.RequestCompile");
    private static ExactSearchExecutionRequest PairResumeRequest(ExactSearchExecutionRequest original, ulong start) =>
        ExactSearchExecutionRequestFactory.Compile(original.CompiledSearch, original.RunOptions with {
            StartSeed = Beta110SeedCodec.FormatOrdinal(start), ScanCount = checked(original.ScanCount - (long)start)
        }).Plan ?? throw new InvalidOperationException("DensePairFixture.ResumeRequestCompile");
    private static ProductionSearchSession PairSession(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> families,
        Func<ExactSearchExecutionRequest, TrustedRootHashInput, ProductionExactSearchResult>? evaluator = null,
        Func<int, CancellationToken, ValueTask>? beforePublish = null, SearchContinuationSnapshot? continuation = null)
    {
        var execution = FamilyPlanner.InOrder(families) with { AllowSmallCandidateExact = true };
        return new(request, execution, FamilySearchEtaProjectorV1.Project(request, execution), 16, 128, null, "",
            exactEvaluator: evaluator, beforeCandidatePublish: beforePublish, continuation: continuation);
    }
    private enum PairCorruption { None, FirstOwner, SecondBatch }
    private sealed class DensePairFixture(bool grouped, int population, bool emptyInitial = false) : IFamilyInvocation, IDenseFamilyBatchGrouping
    {
        internal int SingleCalls, PairCalls;
        internal bool GpuOwnerObserved, FailedConsumed;
        internal Exception? RightFailure, GroupFailure;
        internal Action? BeforeRightFailure, BeforeGroupFailure;
        internal PairCorruption Corruption;
        internal bool PreferGrouping { get; init; } = true;
        public string FamilyId => "N.DensePairFixture";
        public bool CanGroupDenseBatches => grouped && !FailedConsumed;
        public bool PreferDenseBatchGrouping => CanGroupDenseBatches && PreferGrouping;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, 16, [], "TestOnly");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Resolved(FamilyId, .5, "TestOnly");
        public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId, FamilyId, "TestOnly", 1, "TestOnly", usesGpu: false);
        public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, "", 0, 0, 0,
            SingleCalls + PairCalls * 2, 0, 0, 0, 0, false, "TestOnly");
        internal FamilyCandidateSet Select(SearchBatch batch) => FamilyCandidateSet.FromSortedAbi1(batch,
            Enumerable.Range(0, emptyInitial && batch.BatchBase == 0 ? 0 : Math.Min(population, batch.BatchCandidateCount)).Select(i => (ulong)i).ToArray());
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); SingleCalls++;
            GpuOwnerObserved |= context.GpuOwnerThreadId != 0;
            return ValueTask.FromResult(Select(input.Batch));
        }
        public ValueTask<DenseFamilyBatchGroup> PrepareDensePairAsync(FamilyExecutionContext context,
            FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); PairCalls++;
            GpuOwnerObserved |= context.GpuOwnerThreadId != 0;
            if (GroupFailure is not null) { BeforeGroupFailure?.Invoke(); throw GroupFailure; }
            var left = PreparedFamilyBatch.Ready(Corruption == PairCorruption.FirstOwner ? new FixtureFamily("wrong-owner", 1) : this, Select(first.Batch));
            var rightBatch = Corruption == PairCorruption.SecondBatch ? new SearchBatch(second.Batch.BatchBase + 1, second.Batch.BatchCandidateCount) : second.Batch;
            var right = RightFailure is null ? PreparedFamilyBatch.Ready(this, Select(rightBatch)) :
                PreparedFamilyBatch.Failed(this, rightBatch, RightFailure, () => { BeforeRightFailure?.Invoke(); FailedConsumed = true; });
            return ValueTask.FromResult(new DenseFamilyBatchGroup(left, right));
        }
    }
}
