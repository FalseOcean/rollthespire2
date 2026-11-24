using System.Diagnostics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class MerchantShopColorlessGpuExecutor
{
    private FamilyCandidateSet ExecutePublicWindows(FamilyCandidateSet input, CancellationToken token,
        out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        var canonical = Stopwatch.StartNew();
        var windows = new List<FamilyCandidateSet>();
        metrics = default;
        int total = 0;
        int capacity = input.IsDense ? SurvivorCapacity : Math.Min(CompactInputCapacity, SurvivorCapacity);
        for (int offset = 0; offset < input.Count;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(capacity, input.Count - offset);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var preparation = Stopwatch.StartNew();
            // Dense shaders address from zero. Only their private dispatch range
            // changes; compact ordinals retain the original batch mapping.
            FamilyCandidateSet window = input.IsDense
                ? FamilyCandidateSet.Dense(new SearchBatch(checked(input.Batch.BatchBase + (ulong)offset), count))
                : FamilyCandidateSet.FromSortedAbi1(input.Batch, input.ExportAbi1().Slice(offset, count).ToArray());
            double preparationMs = preparation.Elapsed.TotalMilliseconds;
            long preparationBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            FamilyCandidateSet result = ExecuteCore(window, count, token, out var current)!;
            windows.Add(result);
            total = checked(total + result.Count);
            metrics = Sum(metrics, current) with
            {
                UploadMs = metrics.UploadMs + current.UploadMs + preparationMs,
                HostAllocatedBytes = metrics.HostAllocatedBytes + current.HostAllocatedBytes + preparationBytes
            };
            offset = checked(offset + count);
        }

        token.ThrowIfCancellationRequested();
        long mergeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var merge = Stopwatch.StartNew();
        var survivors = new ulong[total];
        int output = 0;
        foreach (FamilyCandidateSet window in windows)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<ulong> ordinals = window.ExportAbi1().Span;
            ulong rebase = input.IsDense ? checked(window.Batch.BatchBase - input.Batch.BatchBase) : 0UL;
            for (int i = 0; i < ordinals.Length; i++)
                survivors[output++] = checked(ordinals[i] + rebase);
        }
        // Each window is sorted, and the input windows are disjoint and ordered.
        // Concatenation preserves order; the full ABI1 validator checks boundaries.
        FamilyCandidateSet combined = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors);
        token.ThrowIfCancellationRequested();
        metrics = metrics with
        {
            ValidationCandidateSetMs = metrics.ValidationCandidateSetMs + merge.Elapsed.TotalMilliseconds,
            HostAllocatedBytes = metrics.HostAllocatedBytes + GC.GetAllocatedBytesForCurrentThread() - mergeAllocated,
            CanonicalAbi1ReadyMs = canonical.Elapsed.TotalMilliseconds
        };
        return combined;
    }

    private static MerchantShopColorlessGpuBatchMetrics Sum(MerchantShopColorlessGpuBatchMetrics a,
        MerchantShopColorlessGpuBatchMetrics b) => new(
        b.CompactionPath,
        checked(a.InputCandidates + b.InputCandidates), checked(a.ProcessedCandidates + b.ProcessedCandidates),
        checked(a.Survivors + b.Survivors),
        a.UploadMs + b.UploadMs, a.CommandMs + b.CommandMs, a.SubmitMs + b.SubmitMs, a.SyncMs + b.SyncMs,
        a.HeaderReadbackMs + b.HeaderReadbackMs, a.PayloadReadbackMs + b.PayloadReadbackMs,
        a.TypedAllocationMs + b.TypedAllocationMs, a.DecodePayloadMs + b.DecodePayloadMs,
        a.SortMs + b.SortMs, a.ValidationCandidateSetMs + b.ValidationCandidateSetMs,
        a.PayloadReadbackAllocatedBytes + b.PayloadReadbackAllocatedBytes, a.HostAllocatedBytes + b.HostAllocatedBytes,
        a.HeaderReadbackBytes + b.HeaderReadbackBytes, a.PayloadReadbackBytes + b.PayloadReadbackBytes,
        a.StablePhase1CommandMs + b.StablePhase1CommandMs, a.StablePhase1SubmitMs + b.StablePhase1SubmitMs,
        a.StablePhase1SyncMs + b.StablePhase1SyncMs, a.StableCountsReadbackMs + b.StableCountsReadbackMs,
        a.StableHostPrefixMs + b.StableHostPrefixMs, a.StableOffsetsUploadMs + b.StableOffsetsUploadMs,
        a.StableScatterCommandMs + b.StableScatterCommandMs, a.StableScatterSubmitMs + b.StableScatterSubmitMs,
        a.StableScatterSyncMs + b.StableScatterSyncMs, a.StableCountsReadbackBytes + b.StableCountsReadbackBytes,
        a.CanonicalAbi1ReadyMs + b.CanonicalAbi1ReadyMs, checked(a.Dispatches + b.Dispatches));
}
