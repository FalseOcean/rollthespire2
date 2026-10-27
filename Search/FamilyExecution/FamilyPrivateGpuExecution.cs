namespace RolltheSpire2.Search.FamilyExecution;

// Thin binding to one existing Family executor. It is neither a serialized stage
// representation nor a scheduler: callers own an ordered list of whole Families.
internal readonly record struct FamilyPrivateGpuOutcome(
    int Count, FamilyCandidateSet? PublicOutput, double DispatchSyncMs,
    double ReportedReadbackMs, long ReadbackBytes, double CanonicalMs, int Dispatches)
{
    // Optional complete, validated ABI1 mirror of this invocation's private output.
    // It does not assert coverage of any unexecuted suffix or escape this batch.
    internal FamilyCandidateSet? SmallOutput { get; init; }
}

internal sealed class FamilyPrivateGpuExecution(
    IDisposable owner, string device, double setupMs,
    PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output,
    Func<SearchBatch, int, CancellationToken, FamilyPrivateGpuOutcome> invoke) : IDisposable
{
    private bool _disposed;
    internal NeowFamilyGpuExecutor? NeowExecutor => owner as NeowFamilyGpuExecutor;
    internal string Device => device;
    internal double SetupMs => setupMs;
    internal FamilyPrivateGpuOutcome Execute(SearchBatch batch, int count, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity ||
            (input is null && count != batch.BatchCandidateCount)) throw new InvalidDataException("FamilyPrivate.InputBounds");
        input?.CheckPopulation(batch, count);
        output?.CheckPopulation(batch, count);
        if (input is not null && ReferenceEquals(input, output)) throw new InvalidDataException("FamilyPrivate.BufferAlias");
        var result = invoke(batch, count, token);
        return CompletePrepared(batch, count, result);
    }
    internal FamilyPrivateGpuOutcome CompletePrepared(SearchBatch batch, int count, FamilyPrivateGpuOutcome result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity ||
            (input is null && count != batch.BatchCandidateCount))
            throw new InvalidDataException("FamilyPrivate.PreparedInputBounds");
        input?.CheckPopulation(batch, count);
        output?.CheckPopulation(batch, count);
        if (input is not null && ReferenceEquals(input, output)) throw new InvalidDataException("FamilyPrivate.BufferAlias");
        if (result.Count < 0 || result.Count > count ||
            (output is null && (result.PublicOutput is null || result.PublicOutput.Batch != batch || result.PublicOutput.Count != result.Count)) ||
            (output is not null && result.PublicOutput is not null)) throw new InvalidDataException("FamilyPrivate.OutputInvariant");
        if (result.SmallOutput is { } small && (output is null || !SmallCandidateExactPolicy.ShouldHandoff(result.Count) ||
            small.IsDense || small.Batch != batch || small.Count != result.Count))
            throw new InvalidDataException("FamilyPrivate.SmallOutputInvariant");
        return result;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        owner.Dispose();
    }
}
