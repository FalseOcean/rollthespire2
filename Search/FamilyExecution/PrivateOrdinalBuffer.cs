using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded composition transport only. No RNG facts and no public ABI change.
// Producer/consumer kernels and their count/header contracts remain private.
internal sealed class PrivateOrdinalBuffer : IDisposable
{
    internal const int Capacity = 1 << 24;
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = [];
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private Rid _smallReadbackBank;
    private FamilyGpuReadback.Pending? _smallReadbackPending;
    private bool _smallReadbackSubmitted;
    private bool _disposed;
    internal Rid Buffer { get; }
    internal PrivateOrdinalBuffer(RenderingDevice rd)
    {
        _rd = rd;
        Buffer = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * 4);
        _owned.Add(Buffer);
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
        if (_smallReadbackSubmitted) throw new InvalidOperationException("PrivateOrdinal.ReadbackAwaitingSync");
    }
    // Verification shares the production bounds and canonical ABI validator.
    // It is deliberately outside the measured execution path.
    internal void VerifyPopulation(SearchBatch batch, int count, FamilyCandidateSet expected)
    {
        var actual = ReadForVerification(batch, count);
        if (!expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()))
            throw new InvalidDataException($"PrivateOrdinal.FullPopulationMismatch:actual={actual.Count};expected={expected.Count};actualHead={string.Join(',', actual.EnumerateLogicalOrdinals().Take(8))};expectedHead={string.Join(',', expected.EnumerateLogicalOrdinals().Take(8))}");
    }
    // The small production handoff materializes the current population as ABI1;
    // it does not imply that any unexecuted suffix Family has checked it.
    internal FamilyCandidateSet ReadAbi1ForExact(SearchBatch batch, int count)
    {
        if (!SmallCandidateExactPolicy.ShouldHandoff(count))
            throw new InvalidDataException("PrivateOrdinal.ExactHandoffCountOutsidePolicy");
        CheckPopulation(batch, count);
        if (!_smallReadbackBank.IsValid)
        {
            _smallReadbackBank = FamilyGpuComputeUtility.AllocateStorageBuffer(_rd,
                SmallCandidateExactPolicy.MaximumCandidates * sizeof(uint), "PrivateOrdinal.ExactHandoff");
            _owned.Add(_smallReadbackBank);
        }
        uint byteCount = checked((uint)count * sizeof(uint));
        long started = Stopwatch.GetTimestamp();
        Exception? copyFailure = null;
        try
        {
            Error error = _rd.BufferCopy(Buffer, _smallReadbackBank, 0, 0, byteCount);
            if (error != Error.Ok)
                copyFailure = new InvalidOperationException($"PrivateOrdinal.ExactHandoffCopyFailed:batch={batch.BatchBase};count={count};error={error}");
        }
        catch (Exception failure)
        {
            copyFailure = new InvalidOperationException($"PrivateOrdinal.ExactHandoffCopyFailed:batch={batch.BatchBase};count={count}", failure);
        }
        double copyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        // Schedule the bounded bank download in this same submission. Keep its
        // callback owner alive until Sync, including exceptional disposal drain.
        if (copyFailure is null)
            _smallReadbackPending = FamilyGpuReadback.Queue(_rd, _smallReadbackBank, 0, byteCount,
                $"PrivateOrdinal.ExactHandoff:batch={batch.BatchBase};count={count}");
        // Copy commands are queued outside a compute list. Complete their own
        // owner-thread Submit/Sync before reading or freeing the small bank.
        // Defer even a copy failure until drain: a managed failure cannot prove
        // whether the native call recorded work. Never read a stale bank.
        // There is no cancellation boundary inside this pair.
        double submitMs, syncMs;
        try
        {
            started = Stopwatch.GetTimestamp();
            _rd.Submit();
            _smallReadbackSubmitted = true;
            submitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            _rd.Sync();
            _smallReadbackSubmitted = false;
            syncMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        catch (Exception drainFailure)
        {
            if (copyFailure is not null)
                throw new AggregateException("PrivateOrdinal.ExactHandoffCopyAndDrainFailed", copyFailure, drainFailure);
            throw;
        }
        if (copyFailure is not null) ExceptionDispatchInfo.Capture(copyFailure).Throw();
        started = Stopwatch.GetTimestamp();
        byte[] bytes;
        try { bytes = _smallReadbackPending!.GetData(); }
        finally { _smallReadbackPending = null; }
        double downloadCollectMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (Bootstrap.RuntimeLog.DetailEnabled)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"privateOrdinalHandoff=true;batchBase={batch.BatchBase};count={count};copyMs={copyMs};submitMs={submitMs};syncMs={syncMs};downloadCollectMs={downloadCollectMs};downloadSourceCapacityBytes={SmallCandidateExactPolicy.MaximumCandidates * sizeof(uint)};requestedBytes={byteCount};transport={FamilyGpuReadback.Revision}");
        return DecodeOrderedAbi1(batch, count, bytes);
    }

    // Explicit full-population verification stays outside measured execution.
    internal FamilyCandidateSet ReadForVerification(SearchBatch batch, int count) => ReadOrderedAbi1(batch, count);

    private FamilyCandidateSet ReadOrderedAbi1(SearchBatch batch, int count)
    {
        CheckPopulation(batch, count);
        return DecodeOrderedAbi1(batch, count,
            count == 0 ? [] : _rd.BufferGetData(Buffer, 0, checked((uint)count * sizeof(uint))));
    }
    private static FamilyCandidateSet DecodeOrderedAbi1(SearchBatch batch, int count, byte[] bytes)
    {
        if (bytes.Length != checked(count * sizeof(uint))) throw new InvalidDataException("PrivateOrdinal.PayloadLength");
        ReadOnlySpan<uint> packed = FamilyGpuComputeUtility.UInt32Span(bytes);
        var ordinals = new ulong[count];
        for (int index = 0; index < count; index++) ordinals[index] = packed[index];
        Array.Sort(ordinals);
        return FamilyCandidateSet.FromSortedAbi1(batch, ordinals);
    }
    public void Dispose()
    {
        if (_disposed) return;
        CheckCount(0);
        // If Sync itself failed, disposal attempts to drain that known Submit
        // once before release. A failed drain leaves RIDs for device teardown;
        // freeing resources still referenced by submitted work is unsafe.
        if (_smallReadbackSubmitted)
        {
            _rd.Sync();
            _smallReadbackSubmitted = false;
        }
        _smallReadbackPending = null;
        _disposed = true;
        FamilyGpuComputeUtility.FreeAll(_rd, _owned);
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
