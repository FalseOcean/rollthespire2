using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

[Flags]
internal enum GpuLargeWindowRole { None = 0, Dense = 1, Compact = 2 }

internal static class GpuRootWindow
{
    internal const int Maximum = 1 << 27;
    internal static FilterBodyDescription PrepareStandalone(FilterBodyDescription body, int requested)
    {
        if (requested <= PrivateOrdinalBuffer.Capacity || !body.SupportsStandaloneLargeWindow ||
            body.IsPrivateTransport || !body.UsesGpu) return body;
        int maximum = MaximumFor([body]);
        return maximum <= PrivateOrdinalBuffer.Capacity ? body :
            body with { WindowCapacity = Math.Min(requested, maximum), MaximumGpuRootWindow = maximum };
    }

    internal static int PublicCapacity(FilterBodyDescription body, int count) =>
        body.MaximumGpuRootWindow > PrivateOrdinalBuffer.Capacity && count > PrivateOrdinalBuffer.Capacity
            ? Math.Min(body.WindowCapacity, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count))
            : PrivateOrdinalBuffer.Capacity;

    internal static T BindPublic<T>(string family, int capacity, Func<T> create)
    {
        if (capacity <= PrivateOrdinalBuffer.Capacity)
        {
            if (GpuRootStorageBudget.Current is null) return create();
            try { return create(); }
            catch (Exception e) when (IsStorageRefusal(e)) { throw new GpuRootWindowBindingRefused(family, e); }
        }
        using var budget = new GpuRootStorageBudget(capacity).Enter();
        try { return create(); }
        catch (Exception e) when (IsStorageRefusal(e)) { throw new GpuRootWindowBindingRefused(family, e); }
    }

    // Only the first public stage gets the larger physical dispatch. A suffix
    // keeps its ordinary addressing, upload capacity and per-player contracts.
    internal static async ValueTask<FamilyCandidateSet> FilterPublicTailAsync(IFilterExecution family,
        FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token, bool supportsWideCompactOrdinals = false)
    {
        services.BudgetPublicTailBindings = family.Body.UsesGpu;
        try
        {
            int length = input.Batch.BatchCandidateCount;
            // Some public compact kernels upload full uint ordinals and dispatch
            // only over the survivors. Their buffer limit applies to count.
            if (length <= PrivateOrdinalBuffer.Capacity || input.Count == 0 ||
                supportsWideCompactOrdinals && !input.IsDense && input.Count <= PrivateOrdinalBuffer.Capacity)
                return await family.FilterAsync(services, input, token).ConfigureAwait(false);
            var source = input.IsDense ? ReadOnlyMemory<ulong>.Empty : input.ExportAbi1();
            var result = new List<ulong>();
            int cursor = 0;
            for (int offset = 0; offset < length; offset += PrivateOrdinalBuffer.Capacity)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(PrivateOrdinalBuffer.Capacity, length - offset);
                var batch = new SearchBatch(checked(input.Batch.BatchBase + (ulong)offset), count);
                FamilyCandidateSet local;
                if (input.IsDense) local = FamilyCandidateSet.Dense(batch);
                else
                {
                    int first = cursor;
                    while (cursor < source.Length && source.Span[cursor] < (ulong)(offset + count)) cursor++;
                    if (first == cursor) continue;
                    var ordinals = source.Slice(first, cursor - first).ToArray();
                    for (int i = 0; i < ordinals.Length; i++) ordinals[i] -= (ulong)offset;
                    local = FamilyCandidateSet.FromSortedAbi1(batch, ordinals);
                }
                var filtered = await family.FilterAsync(services, local, token).ConfigureAwait(false);
                if (filtered.Batch != batch) throw new InvalidDataException("PublicGpuTail.ChangedBatch");
                foreach (ulong ordinal in filtered.EnumerateLogicalOrdinals()) result.Add((ulong)offset + ordinal);
            }
            return FamilyCandidateSet.FromSortedAbi1(input.Batch, result.ToArray());
        }
        finally { services.BudgetPublicTailBindings = false; }
    }

    internal static bool IsStorageRefusal(Exception error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is AggregateException) return false;
            if (cause is GpuStorageAllocationRefused) return true;
        }
        return false;
    }


    internal static int MaximumFor(IReadOnlyList<FilterBodyDescription> stages)
    {
        if (stages.Count == 0 ||
            !stages[0].LargeWindowRole.HasFlag(GpuLargeWindowRole.Dense) ||
            stages.Skip(1).Any(s => !s.LargeWindowRole.HasFlag(GpuLargeWindowRole.Compact)))
            return PrivateOrdinalBuffer.Capacity;
        // A terminal R reserves one public ordinal per input. At 2^26 its two
        // principal buffers together are 512 MiB and leave room for later bindings.
        return stages.Count == 1 || stages.Any(s => s.FamilyId == RelicFamily.Id) ? 1 << 26 : Maximum;
    }

    internal static void Dispatch(RenderingDevice rd, long list, uint groups)
    {
        const uint row = 32768;
        uint x = Math.Min(groups, row);
        rd.ComputeListDispatch(list, x, (groups + x - 1) / x, 1);
    }
    internal static string Shader2D(string source)
    {
        if (!source.Contains("gl_GlobalInvocationID.x", StringComparison.Ordinal))
            throw new InvalidOperationException("FamilyGpu.LargeWindowIndexMissing");
        // Every admitted kernel has a 64x1 local size. A second row exists
        // only when X is exactly 32768 groups. D3D12 needs no NumWorkGroups constant.
        return source.Replace("gl_GlobalInvocationID.x", "(gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * 2097152u)", StringComparison.Ordinal)
            .Replace("gl_WorkGroupID.x", "(gl_WorkGroupID.x + gl_WorkGroupID.y * 32768u)", StringComparison.Ordinal);
    }
}

internal sealed class GpuRootWindowBindingRefused(string family, Exception cause)
    : Exception("FamilyGpu.LargeWindowBindingRefused:" + family, cause);
internal sealed class GpuStorageAllocationRefused(string message) : InvalidOperationException(message);

// Large windows run one physical chain at a time. Account for live storage on
// its owner thread and reserve 256 MiB of a 1 GiB envelope for other device costs.
internal sealed class GpuRootStorageBudget(int rootCapacity) : IDisposable
{
    internal int RootCapacity { get; } = rootCapacity;
    private const long Limit = 768L << 20;
    [ThreadStatic] internal static GpuRootStorageBudget? Current;
    private sealed class DeviceStorage
    {
        internal readonly Dictionary<Rid, int> Allocations = [];
        internal long Bytes;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RenderingDevice, DeviceStorage> Devices = new();
    internal GpuRootStorageBudget Enter()
    {
        if (Current is not null) throw new InvalidOperationException("FamilyGpu.NestedRootBudget");
        Current = this;
        return this;
    }
    // Local RenderingDevice queues the buffers retired by FreeRid until a frame
    // completes. Called only after every old GPU invocation has been disposed.
    internal static void DrainRetiredWindowIfNeeded(RenderingDevice rd, int window)
    {
        long recorded = Devices.GetValue(rd, static _ => new DeviceStorage()).Bytes;
        ulong total = rd.GetMemoryUsage(RenderingDevice.MemoryType.Total);
        long nextOrdinalBytes = (long)window * sizeof(uint);
        if (Math.Max((ulong)recorded, total) <= (ulong)(Limit - nextOrdinalBytes)) return;
        ulong buffers = rd.GetMemoryUsage(RenderingDevice.MemoryType.Buffers);
        rd.Submit();
        rd.Sync();
        ulong totalAfter = rd.GetMemoryUsage(RenderingDevice.MemoryType.Total);
        ulong buffersAfter = rd.GetMemoryUsage(RenderingDevice.MemoryType.Buffers);
        RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"adaptiveGpuWindowReleaseDrain=true;window={window};tracked={recorded};buffersBefore={buffers};totalBefore={total};buffersAfter={buffersAfter};totalAfter={totalAfter};ordinalBytes={nextOrdinalBytes};limit={Limit}");
    }
    internal void Check(RenderingDevice rd, int bytes)
    {
        long recorded = Devices.GetValue(rd, static _ => new DeviceStorage()).Bytes;
        ulong total = rd.GetMemoryUsage(RenderingDevice.MemoryType.Total);
        ulong used = Math.Max((ulong)recorded, total);
        if (bytes > Limit || used > (ulong)(Limit - bytes))
            throw new GpuStorageAllocationRefused($"FamilyGpu.LargeWindowStorageBudget:live={used};additional={bytes};limit={Limit};tracked={recorded};rdTotal={total}");
    }
    internal void Add(RenderingDevice rd, Rid rid, int bytes)
    {
        var storage = Devices.GetValue(rd, static _ => new DeviceStorage());
        storage.Allocations.Add(rid, bytes);
        storage.Bytes += bytes;
    }
    internal static void Release(RenderingDevice rd, Rid rid)
    {
        if (Devices.TryGetValue(rd, out var storage) && storage.Allocations.Remove(rid, out int bytes))
            storage.Bytes -= bytes;
    }
    public void Dispose() => Current = null;
}
