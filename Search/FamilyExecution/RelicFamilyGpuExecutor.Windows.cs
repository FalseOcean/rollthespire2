using System.Diagnostics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class RelicFamilyGpuExecutor
{
    private FamilyCandidateSet ExecutePublicCompactWindows(FamilyCandidateSet input, CancellationToken token,
        out RelicFamilyGpuBatchMetrics metrics)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        var canonical = Stopwatch.StartNew();
        var windows = new List<FamilyCandidateSet>();
        metrics = default;
        int total = 0;
        uint routeA = 0, routeB = 0, earlyHeavy = 0, lateHeavy = 0, earlyPass = 0, latePass = 0;
        for (int offset = 0; offset < input.Count;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(CompactInputCapacity, input.Count - offset);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var preparation = Stopwatch.StartNew();
            // Keep the original batch/base and its ordinals: slicing the transfer
            // buffer must not renumber candidates from an upstream family.
            var window = FamilyCandidateSet.FromSortedAbi1(input.Batch, input.ExportAbi1().Slice(offset, count).ToArray());
            double preparationMs = preparation.Elapsed.TotalMilliseconds;
            long preparationBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            FamilyCandidateSet result = ExecuteCore(window, count, token, out var current)!;
            windows.Add(result);
            total = checked(total + result.Count);
            routeA = checked(routeA + LastRouteA); routeB = checked(routeB + LastRouteB);
            earlyHeavy = checked(earlyHeavy + LastEarlyHeavy); lateHeavy = checked(lateHeavy + LastLateHeavy);
            earlyPass = checked(earlyPass + LastEarlyPass); latePass = checked(latePass + LastLatePass);
            metrics = Sum(metrics, current) with
            {
                CompactPackConvertValidateMs = metrics.CompactPackConvertValidateMs + current.CompactPackConvertValidateMs + preparationMs,
                CompactPackAllocatedBytes = metrics.CompactPackAllocatedBytes + current.CompactPackAllocatedBytes + preparationBytes
            };
            offset = checked(offset + count);
        }

        token.ThrowIfCancellationRequested();
        var survivors = new ulong[total];
        int output = 0;
        foreach (FamilyCandidateSet window in windows)
        {
            token.ThrowIfCancellationRequested();
            window.ExportAbi1().Span.CopyTo(survivors.AsSpan(output));
            output = checked(output + window.Count);
        }
        // Individually sorted slices of an ordered input remain globally ordered.
        // Validate the final stream against the original batch, including joins.
        FamilyCandidateSet combined = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors);
        token.ThrowIfCancellationRequested();
        LastRouteA = routeA; LastRouteB = routeB;
        LastEarlyHeavy = earlyHeavy; LastLateHeavy = lateHeavy;
        LastEarlyPass = earlyPass; LastLatePass = latePass;
        metrics = metrics with { CanonicalAbi1ReadyMs = canonical.Elapsed.TotalMilliseconds };
        return combined;
    }

    private static RelicFamilyGpuBatchMetrics Sum(RelicFamilyGpuBatchMetrics a, RelicFamilyGpuBatchMetrics b) => new(
        checked(a.InputCandidates + b.InputCandidates), checked(a.ProcessedCandidates + b.ProcessedCandidates),
        checked(a.Survivors + b.Survivors), checked(a.DispatchGroups + b.DispatchGroups),
        a.MetadataUploadMs + b.MetadataUploadMs,
        a.CompactPackAllocationMs + b.CompactPackAllocationMs, a.CompactPackConvertValidateMs + b.CompactPackConvertValidateMs,
        a.CompactPackAllocatedBytes + b.CompactPackAllocatedBytes, a.CompactUploadStagingMs + b.CompactUploadStagingMs,
        a.CompactUploadStagingAllocatedBytes + b.CompactUploadStagingAllocatedBytes,
        a.CompactBufferUpdateMs + b.CompactBufferUpdateMs, a.CommandMs + b.CommandMs,
        a.SubmitMs + b.SubmitMs, a.SyncMs + b.SyncMs, a.ReadbackMs + b.ReadbackMs,
        a.ReadbackBytes + b.ReadbackBytes, a.CanonicalAbi1ReadyMs + b.CanonicalAbi1ReadyMs);
}
