using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>A batch owns the only global-to-local candidate identity mapping.</summary>
public readonly record struct SearchBatch
{
    public SearchBatch(ulong batchBase, int batchCandidateCount)
    {
        if (batchCandidateCount < 0) throw new ArgumentOutOfRangeException(nameof(batchCandidateCount));
        if ((ulong)batchCandidateCount > ulong.MaxValue - batchBase)
            throw new ArgumentOutOfRangeException(nameof(batchCandidateCount), "SearchBatch range overflows UInt64.");
        BatchBase = batchBase;
        BatchCandidateCount = batchCandidateCount;
    }

    public ulong BatchBase { get; }
    public int BatchCandidateCount { get; }

    public ulong GlobalCandidate(ulong logicalOrdinal)
    {
        if (logicalOrdinal >= (ulong)BatchCandidateCount)
            throw new ArgumentOutOfRangeException(nameof(logicalOrdinal));
        return checked(BatchBase + logicalOrdinal);
    }
}

/// <summary>
/// ABI1 is batch-local LogicalOrdinal[] only. Dense is an implicit physical input;
/// compact Family outputs carry the same unrenumbered logical ordinals explicitly.
/// </summary>
public sealed class FamilyCandidateSet
{
    private readonly ulong[]? _abi1;

    private FamilyCandidateSet(SearchBatch batch, ulong[]? abi1)
    {
        Batch = batch;
        _abi1 = abi1;
    }

    public SearchBatch Batch { get; }
    public bool IsDense => _abi1 is null;
    public int Count => _abi1?.Length ?? Batch.BatchCandidateCount;

    public static FamilyCandidateSet Dense(SearchBatch batch) => new(batch, null);

    public static FamilyCandidateSet FromAbi1(SearchBatch batch, IReadOnlyList<ulong> logicalOrdinals)
    {
        ArgumentNullException.ThrowIfNull(logicalOrdinals);
        var abi1 = new ulong[logicalOrdinals.Count];
        for (int index = 0; index < logicalOrdinals.Count; index++)
        {
            ulong logicalOrdinal = logicalOrdinals[index];
            if (logicalOrdinal >= (ulong)batch.BatchCandidateCount)
                throw new ArgumentOutOfRangeException(nameof(logicalOrdinals), "ABI1 ordinal is outside its SearchBatch.");
            if (index > 0 && logicalOrdinal <= abi1[index - 1])
                throw new ArgumentException("ABI1 must be a strictly ordered candidate stream.", nameof(logicalOrdinals));
            abi1[index] = logicalOrdinal;
        }
        return new FamilyCandidateSet(batch, abi1);
    }

    internal static FamilyCandidateSet FromSortedAbi1(SearchBatch batch, ulong[] logicalOrdinals)
    {
        ArgumentNullException.ThrowIfNull(logicalOrdinals);
        for (int index = 0; index < logicalOrdinals.Length; index++)
        {
            ulong logicalOrdinal = logicalOrdinals[index];
            if (logicalOrdinal >= (ulong)batch.BatchCandidateCount)
                throw new ArgumentOutOfRangeException(nameof(logicalOrdinals), "ABI1 ordinal is outside its SearchBatch.");
            if (index > 0 && logicalOrdinal <= logicalOrdinals[index - 1])
                throw new ArgumentException("Sorted ABI1 must be strictly increasing.", nameof(logicalOrdinals));
        }
        return new FamilyCandidateSet(batch, logicalOrdinals);
    }

    public IEnumerable<ulong> EnumerateLogicalOrdinals()
    {
        if (_abi1 is not null)
        {
            foreach (ulong logicalOrdinal in _abi1) yield return logicalOrdinal;
            yield break;
        }
        for (ulong logicalOrdinal = 0; logicalOrdinal < (ulong)Batch.BatchCandidateCount; logicalOrdinal++)
            yield return logicalOrdinal;
    }

    public ReadOnlyMemory<ulong> ExportAbi1() => _abi1 is not null
        ? _abi1
        : throw new InvalidOperationException("Dense input is implicit and has no materialized ABI1 buffer.");
}

public sealed record FamilyObservationWindow(
    SearchBatch Batch,
    ExactSearchExecutionRequest ExactRequest);

public interface IFamilyInvocation
{
    string FamilyId { get; }
    IReadOnlyList<string> Coverage => [FamilyId];
    FamilyAnalyticalCostProjection AnalyticalCost { get; }
    FamilySurvivalProjection Survival { get; }
    // Independent replay does not imply probabilistic independence. Only Families
    // with a real shared-fact dependency opt into prefix-conditioned planning.
    bool HasConditionalProjections => false;
    FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage) => Survival;
    FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request) => null;
    internal IEnumerable<IFamilyInvocation> CpuRealizations => [];
    internal bool CanBindPrivateSerial => false;
    // Same-instance, same-input-role subtraction ONLY. A non-null certificate
    // asserts the same numerical/emission body under public/private addressing.
    // Its zero numerical term is cancelled unknown work, never an absolute quote.
    // Setup retains its Family reference or conservative bound; allocation applies reach.
    // Null leaves comparison unknown.
    internal FamilyPhysicalQuote? QuoteCommonPrivateWorkCancellation(FamilyPhysicalQuoteRequest request) => null;
    internal FamilyPrivateGpuExecution BindPrivateSerial(Godot.RenderingDevice device,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output) =>
        throw new InvalidOperationException("FamilyPrivate.BindingNotAdmitted:" + FamilyId);
    FamilyExpectedFilteringCostProjection? ResolveExpectedFilteringCost(IReadOnlySet<string> passedCoverage) => ExpectedFilteringCost;
    FamilyConditionPerformanceProjection ConditionPerformance { get; }
    FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compactAbi1Input) => ConditionPerformance;
    FamilyExpectedFilteringCostProjection? ExpectedFilteringCost => null;
    ValueTask<FamilyCandidateSet> InvokeAsync(
        FamilyExecutionContext executionContext,
        FamilyObservationWindow observationWindow,
        FamilyCandidateSet input,
        CancellationToken cancellationToken);

    FamilyPerformanceObservation CapturePerformanceObservation();
    internal IEnumerable<GpuLocalPeak> CostPeaks => [];
    internal void LogExecutionEvidence() { }

    FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => null;

    ValueTask DisposeAsync(FamilyExecutionContext executionContext) => ValueTask.CompletedTask;
}

public static class LinearFamilyPipeline
{
    public static async ValueTask<FamilyCandidateSet> FilterAsync(
        FamilyExecutionContext executionContext,
        FamilyObservationWindow observationWindow,
        IReadOnlyList<IFamilyInvocation> families,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FamilyCandidateSet> results = await FilterGroupAsync(
            executionContext, [observationWindow], families, cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    // 獨立窗口一起通過篩選鏈；支援合併的 family 共用提交，其餘仍按索引逐次執行。
    public static async ValueTask<IReadOnlyList<FamilyCandidateSet>> FilterGroupAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<IFamilyInvocation> families,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(families);
        if (windows.Count == 0) throw new ArgumentException("At least one window is required.", nameof(windows));

        var current = new FamilyCandidateSet[windows.Count];
        for (int index = 0; index < windows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current[index] = FamilyCandidateSet.Dense(windows[index].Batch);
        }
        return await ApplyFamiliesAsync(
            executionContext, windows, families, current, cancellationToken).ConfigureAwait(false);
    }

    // 接續已完成前段 family 的窗口群組，沿用相同排序與驗證規則。
    public static ValueTask<IReadOnlyList<FamilyCandidateSet>> FilterRemainingGroupAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<IFamilyInvocation> families,
        IReadOnlyList<FamilyCandidateSet> current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(current);
        if (windows.Count == 0) throw new ArgumentException("At least one window is required.", nameof(windows));
        if (current.Count != windows.Count)
            throw new InvalidOperationException("FamilyPipelineCurrentCountMismatch");
        for (int index = 0; index < windows.Count; index++)
        {
            if (current[index].Batch != windows[index].Batch)
                throw new InvalidOperationException("FamilyPipelineBatchIdentityMismatch");
        }
        var copy = new FamilyCandidateSet[windows.Count];
        for (int index = 0; index < windows.Count; index++) copy[index] = current[index];
        return ApplyFamiliesAsync(executionContext, windows, families, copy, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<FamilyCandidateSet>> ApplyFamiliesAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<IFamilyInvocation> families,
        FamilyCandidateSet[] current,
        CancellationToken cancellationToken)
    {
        foreach (IFamilyInvocation family in families)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FamilyCandidateSet[] next = new FamilyCandidateSet[windows.Count];
            bool merged = false;
            if (windows.Count > 1 && family is IFamilyBatchInvocation batchInvocation &&
                batchInvocation.BatchGroupCapacity >= windows.Count)
            {
                try
                {
                    IReadOnlyList<FamilyCandidateSet> batched = await batchInvocation.InvokeBatchAsync(
                        executionContext, windows, current, cancellationToken).ConfigureAwait(false);
                    if (batched.Count != windows.Count)
                        throw new InvalidOperationException("FamilyBatchResultCountMismatch:" + family.FamilyId);
                    for (int index = 0; index < windows.Count; index++) next[index] = batched[index];
                    merged = true;
                }
                catch (FamilyBatchNotSupportedException)
                {
                    // 尚未提交，可安全退回逐窗口執行。
                    merged = false;
                }
            }
            if (!merged)
            {
                for (int index = 0; index < windows.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    next[index] = await family.InvokeAsync(
                        executionContext, windows[index], current[index], cancellationToken).ConfigureAwait(false);
                }
            }
            if (family.CapturePerformanceObservation().EligibilityEvidence == "PhysicalFailureOrRecovery")
                throw new InvalidOperationException("PhysicalRecoveryRejected:" + family.FamilyId);
            for (int index = 0; index < windows.Count; index++)
            {
                if (next[index].Batch != windows[index].Batch)
                    throw new InvalidOperationException("Family changed SearchBatch identity.");
                ValidateOrderedSubsequence(current[index], next[index]);
            }
            current = next;
        }

        return current;
    }

    private static void ValidateOrderedSubsequence(FamilyCandidateSet input, FamilyCandidateSet output)
    {
        if (input.IsDense) return;
        if (output.IsDense)
            throw new InvalidOperationException("A compact Family input cannot expand back to Dense.");

        ReadOnlySpan<ulong> source = input.ExportAbi1().Span;
        ReadOnlySpan<ulong> filtered = output.ExportAbi1().Span;
        int sourceIndex = 0;
        for (int outputIndex = 0; outputIndex < filtered.Length; outputIndex++)
        {
            ulong candidate = filtered[outputIndex];
            while (sourceIndex < source.Length && source[sourceIndex] < candidate)
                sourceIndex++;
            if (sourceIndex >= source.Length || source[sourceIndex] != candidate)
                throw new InvalidOperationException("Family output is not a subsequence of its input stream.");
            sourceIndex++;
        }
    }
}
