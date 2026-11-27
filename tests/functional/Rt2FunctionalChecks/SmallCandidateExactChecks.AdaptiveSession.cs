using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class SmallCandidateExactChecks
{
    private static async Task CheckAdaptiveSessionRecoveryAsync(PreparedSearch template)
    {
        const int small = PrivateOrdinalBuffer.Capacity;
        const ulong start = 37;
        const int count = 96 * small + 7;
        var request = SearchPreparationCompiler.Compile(template.Model.CompiledSearch, template.RunOptions with
        {
            StartSeed = Beta110SeedCodec.FormatOrdinal(start), ScanCount = count, TargetMatchCount = 10000,
            GpuRootBatchSize = 0
        }).Plan!;
        SearchContinuationSnapshot? savedFault = null;
        async Task<(string[] Seeds, ulong Cursor)> Run(bool adaptive, bool cleanupFails, SearchContinuationSnapshot? resume = null)
        {
            var lifecycle = new List<string>();
            var executed = new List<(int Generation, SearchBatch Batch, bool Right)>();
            int generation = 0;
            var body = new FilterBodyDescription(new PreparedFilter("window-probe"), "window-probe", RuntimeProfileId.Beta111)
            {
                UsesGpu = true, WindowCapacity = small, SupportsStandaloneLargeWindow = true, LargeWindowRole = GpuLargeWindowRole.Dense,
                CreateExecution = (selected, _) =>
                {
                    lifecycle.Add($"create:{++generation}:{selected.WindowCapacity}");
                    return new AdaptiveSessionFixture(selected, generation, adaptive, cleanupFails, lifecycle, executed);
                }
            };
            var runRequest = resume is null ? request : SearchPreparationCompiler.Compile(request.Model.CompiledSearch,
                request.RunOptions with { StartSeed = Beta110SeedCodec.FormatOrdinal(resume.NextOrdinal),
                    ScanCount = checked(count - (long)(resume.NextOrdinal - start)) }).Plan!;
            var plan = FamilyPlanner.InOrder([body]);
            await using var session = new ProductionSearchSession(runRequest, plan,
                FamilySearchEtaProjectorV1.Project(SearchEstimationPreparation.ForRun(runRequest, plan), plan),
                small, 256, null, "", continuation: resume, enableAdaptiveGpuBatch: adaptive,
                executionContextFactory: () => new FilterRuntimeServices(requiresGpu: false));
            await session.Completion.WaitAsync(PairTimeout);
            var progress = session.GetProgress();
            var hits = Drain(session);
            foreach (var hit in hits) session.AcknowledgeCandidate(hit.Seed);
            ulong cursor = session.CaptureContinuation()!.NextOrdinal;
            if (adaptive)
            {
                var refusal = executed.Single(entry => entry.Generation == 2);
                Check(refusal.Batch.BatchCandidateCount == 2 * small && executed.Any(e => e.Generation == 1 && e.Right) &&
                    lifecycle.Take(4).SequenceEqual(new[] { $"create:1:{small}", "release:1", $"create:2:{2*small}", "release:2" }),
                    "real automatic session drains paired baseline and retires the 32M trial after storage refusal");
                if (cleanupFails)
                {
                    savedFault = session.CaptureContinuation();
                    Check(generation == 2 && progress.State == SearchRunState.Faulted && progress.FailureCode.Contains("ReleaseFailed") &&
                        progress.FailureCode.Contains("wide-slot-release-failed") && progress.FailureCode.Contains("LargeWindowBindingRefused") &&
                        cursor <= refusal.Batch.BatchBase && progress.ScannedCount <= (long)(refusal.Batch.BatchBase - start),
                        $"cleanup failure stops at refused frontier;generation={generation};state={progress.State};cursor={cursor};refusal={refusal.Batch.BatchBase};scanned={progress.ScannedCount};code={progress.FailureCode}");
                }
                else
                {
                    var retry = executed.First(e => e.Generation == 3);
                    Check(retry.Batch == new SearchBatch(refusal.Batch.BatchBase, small) && executed.Any(e => e.Generation == 3 && e.Right),
                        "same rejected frontier retries at 16M and restores ordinary two-window admission");
                }
            }
            if (!cleanupFails)
                Check(progress.State == SearchRunState.Completed && cursor == start + count && (resume is not null || progress.ScannedCount == count && hits.Length == 97) && hits.All(hit => !hit.IsUnverified),
                    "every small window, including prepared right members and partial tail, delivers exactly one Exact");
            return (hits.Select(hit => hit.Seed).Order(StringComparer.Ordinal).ToArray(), cursor);
        }
        var baseline = await Run(false, false);
        var recovered = await Run(true, false);
        Check(baseline.Cursor == recovered.Cursor && baseline.Seeds.SequenceEqual(recovered.Seeds),
            "adaptive refusal and the fixed baseline deliver identical complete Exact seed sets and final cursor");
        var failed = await Run(true, true);
        var resumed = await Run(false, false, savedFault ?? throw new InvalidOperationException("Missing fault continuation"));
        Check(failed.Seeds.Concat(resumed.Seeds).Order(StringComparer.Ordinal).SequenceEqual(baseline.Seeds) &&
            resumed.Cursor == baseline.Cursor, $"fault+resume missing={string.Join(',',baseline.Seeds.Except(failed.Seeds.Concat(resumed.Seeds)))} duplicate={string.Join(',',failed.Seeds.Intersect(resumed.Seeds))} counts={failed.Seeds.Length}+{resumed.Seeds.Length};cursor={resumed.Cursor}/{baseline.Cursor};resumeBase={failed.Cursor}");
    }

    private sealed class AdaptiveSessionFixture(FilterBodyDescription body, int generation, bool adaptive, bool cleanupFails,
        List<string> lifecycle, List<(int Generation, SearchBatch Batch, bool Right)> executed) : IFilterExecution, IDenseFamilyBatchGrouping
    {
        public FilterBodyDescription Body => body;
        public bool ExecutionFaulted => false;
        public bool CanGroupDenseBatches => body.MaximumGpuRootWindow == 0;
        public bool PreferDenseBatchGrouping => CanGroupDenseBatches;
        private FamilyCandidateSet Execute(SearchBatch batch, bool right)
        {
            executed.Add((generation, batch, right));
            if (generation == 2)
                throw new GpuRootWindowBindingRefused("window-probe", new GpuStorageAllocationRefused("wide-storage-refusal"));
            const ulong first = 37, period = PrivateOrdinalBuffer.Capacity;
            ulong offset = (period - (batch.BatchBase - first) % period) % period;
            return FamilyCandidateSet.FromSortedAbi1(batch, offset < (ulong)batch.BatchCandidateCount ? [offset] : []);
        }
        public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        {
            if (adaptive && generation == 1) await Task.Delay(22, token);
            return Execute(input.Batch, false);
        }
        public async ValueTask<DenseFamilyBatchGroup> PrepareDensePairAsync(FilterRuntimeServices services,
            SearchBatch first, SearchBatch second, CancellationToken token)
        {
            if (adaptive && generation == 1) await Task.Delay(44, token);
            return new(PreparedFamilyBatch.Ready(this, Execute(first, false)), PreparedFamilyBatch.Ready(this, Execute(second, true)));
        }
        public ValueTask DisposeAsync(FilterRuntimeServices services)
        {
            lifecycle.Add($"release:{generation}");
            if (generation == 2 && cleanupFails) throw new IOException("wide-slot-release-failed");
            return ValueTask.CompletedTask;
        }
    }
}
