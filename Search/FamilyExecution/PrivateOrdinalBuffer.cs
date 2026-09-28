using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded composition transport only. No RNG facts and no public ABI change.
// Producer/consumer kernels and their count/header contracts remain private.
internal sealed class PrivateOrdinalBuffer : IDisposable
{
    internal const int Capacity = 1 << 24;
    private readonly RenderingDevice _rd;
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private bool _disposed;
    internal Rid Buffer { get; }
    internal PrivateOrdinalBuffer(RenderingDevice rd)
    {
        _rd = rd;
        Buffer = FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * 4);
    }
    internal void CheckCount(int count)
    {
        if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("PrivateOrdinal.OwnerMismatch");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count < 0 || count > Capacity) throw new InvalidDataException("PrivateOrdinal.CountOutsideCapacity");
    }
    internal void CheckPopulation(SearchBatch batch, int count)
    {
        CheckCount(batch.BatchCandidateCount);
        CheckCount(count);
        if (count > batch.BatchCandidateCount) throw new InvalidDataException("PrivateOrdinal.CountOutsideBatch");
    }
    // Verification shares the production bounds and canonical ABI validator.
    // It is deliberately outside the measured execution path.
    internal void VerifyPopulation(SearchBatch batch, int count, FamilyCandidateSet expected)
    {
        var actual = ReadForVerification(batch, count);
        if (!expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()))
            throw new InvalidDataException($"PrivateOrdinal.FullPopulationMismatch:actual={actual.Count};expected={expected.Count};actualHead={string.Join(',', actual.EnumerateLogicalOrdinals().Take(8))};expectedHead={string.Join(',', expected.EnumerateLogicalOrdinals().Take(8))}");
    }
    // Explicit verification runs only; ordinary execution never reads this payload.
    internal FamilyCandidateSet ReadForVerification(SearchBatch batch, int count)
    {
        CheckPopulation(batch, count);
        uint[] raw = count == 0 ? [] : FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(Buffer, 0, checked((uint)count * 4)));
        if (raw.Length != count) throw new InvalidDataException("PrivateOrdinal.PayloadLength");
        Array.Sort(raw);
        return FamilyCandidateSet.FromSortedAbi1(batch, raw.Select(x => (ulong)x).ToArray());
    }
    public void Dispose()
    {
        if (_disposed) return;
        CheckCount(0); _disposed = true; _rd.FreeRid(Buffer);
    }
    // Release borrowers before this buffer, even if an earlier release fails.
    // The caller supplies dependency order; no resource scheduler or hidden recovery.
    internal static void DisposeAll(params IDisposable?[] resources)
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            try { resource?.Dispose(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is not null) throw new AggregateException("PrivateOrdinal.CleanupFailed", failures);
    }
}
