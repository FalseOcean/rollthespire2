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

[Flags]
internal enum GpuNumericRequirements
{
    None = 0,
    Int64 = 1,
    Float64 = 2,
    Unspecified = 4
}

internal static class LinearFamilyPipeline
{
    public static ValueTask<FamilyCandidateSet> FilterAsync(
        FilterRuntimeServices executionContext,
        SearchBatch batch,
        IReadOnlyList<IFilterExecution> families,
        CancellationToken cancellationToken) =>
        FilterAsync(executionContext, batch, families, null, cancellationToken);

    internal static async ValueTask<FamilyCandidateSet> FilterAsync(
        FilterRuntimeServices executionContext,
        SearchBatch batch,
        IReadOnlyList<IFilterExecution> families,
        SmallCandidateExactPolicy? smallCandidateExact,
        CancellationToken cancellationToken,
        PreparedFamilyBatch? preparedFirst = null)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(families);

        FamilyCandidateSet current = FamilyCandidateSet.Dense(batch);
        for (int familyIndex = 0; familyIndex < families.Count; familyIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Count == 0 || smallCandidateExact is not null && SmallCandidateExactPolicy.ShouldHandoff(current.Count))
            {
                smallCandidateExact?.Observe(familyIndex == 0 ? "Dense" :
                    string.Join(',', families.Take(familyIndex).SelectMany(item => item.Body.Coverage)),
                    families.Skip(familyIndex).SelectMany(item => item.Body.Coverage).Distinct(StringComparer.Ordinal).ToArray(),
                    current.Count);
                return current.IsDense
                    ? FamilyCandidateSet.FromSortedAbi1(current.Batch, current.EnumerateLogicalOrdinals().ToArray())
                    : current;
            }
            IFilterExecution family = families[familyIndex];
            // A failed CPU matcher has no private shared state. Stop the tail too:
            // its numerical prerequisites may assume the missing prefix passed.
            IReadOnlyList<string> RemainingCoverage() => families.Skip(familyIndex).SelectMany(item => item.Body.Coverage)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (executionContext.TryGetCpuFilterIsolation(family, out var isolated))
            {
                executionContext.RecordCpuFilterIsolation(family, batch, current, RemainingCoverage(), isolated);
                return current;
            }
            if (family.ExecutionFaulted)
            {
                executionContext.Costs.Invalidate(family.Body);
                throw new InvalidOperationException("PhysicalRecoveryRejected:" + family.Body.FamilyId);
            }
            FamilyCandidateSet next;
            try
            {
                next = familyIndex == 0 && preparedFirst is not null
                    ? await preparedFirst.InvokeAsync(family, current, cancellationToken).ConfigureAwait(false)
                    : await family.FilterAsync(executionContext, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && FamilyCpuMatchException.IsRecoverable(error))
            {
                // Execute/Parallel.For has joined every partition before throwing.
                // None of its partial staging arrays escaped the invocation.
                executionContext.Costs.Invalidate(family.Body);
                executionContext.RecordCpuFilterIsolation(family, batch, current, RemainingCoverage(), error);
                return current;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                executionContext.Costs.Invalidate(family.Body);
                throw;
            }
            if (family.ExecutionFaulted)
            {
                executionContext.Costs.Invalidate(family.Body);
                throw new InvalidOperationException("PhysicalRecoveryRejected:" + family.Body.FamilyId);
            }
            if (next.Batch != batch)
                throw new InvalidOperationException("Family changed SearchBatch identity.");
            ValidateOrderedSubsequence(current, next);
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
