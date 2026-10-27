namespace RolltheSpire2.Search.FamilyExecution;

internal static class FamilyDenseBatchGroupingPolicy
{
    // Previous validated population selects only the next physical cadence.
    // Every current count is still collected in full; this never predicts an
    // output, changes q/s/Cost, or grants the small-Exact boundary in advance.
    internal const string DefaultPreference = "ObservedSmallCpuBoundary.20261027.v1";
    internal static readonly bool Enabled =
#if RT2_DISABLE_ROOT_BATCH_PAIRS
        false;
#else
        true;
#endif
}

// A bounded two-root physical prefix, never a stage graph or semantic state.
// Each member returns through the original pipeline before terminal handoff.
internal interface IDenseFamilyBatchGrouping
{
    bool CanGroupDenseBatches { get; }
    // Capability and default cadence are distinct. Explicit native checks may
    // exercise a supported group even when ordinary execution prefers K1.
    bool PreferDenseBatchGrouping => CanGroupDenseBatches;
    ValueTask<DenseFamilyBatchGroup> PrepareDensePairAsync(FamilyExecutionContext context,
        FamilyObservationWindow first, FamilyObservationWindow second, CancellationToken token);
}

internal sealed record DenseFamilyBatchGroup(PreparedFamilyBatch First, PreparedFamilyBatch Second);

// Only an already-synchronized, isolated later member uses this marker. It
// permits earlier terminal work to drain; device-wide failures never use it.
internal sealed class DeferredRootBatchFailure(SearchBatch batch, Exception original)
    : Exception($"RootPair.DeferredMember:batch={batch.BatchBase};cause={original.Message}", original)
{
    internal SearchBatch Batch => batch;
    internal Exception Original => InnerException!;
}

internal sealed class PreparedFamilyBatch(IFamilyInvocation owner, SearchBatch batch,
    Func<CancellationToken, ValueTask<FamilyCandidateSet>> invoke)
{
    private int _used;
    internal SearchBatch Batch => batch;
    internal static PreparedFamilyBatch Ready(IFamilyInvocation owner, FamilyCandidateSet output) =>
        new(owner, output.Batch, _ => ValueTask.FromResult(output));
    internal static PreparedFamilyBatch Failed(IFamilyInvocation owner, SearchBatch batch, Exception failure,
        Action? markFaulted = null) => new(owner, batch, _ =>
        {
            markFaulted?.Invoke();
            return ValueTask.FromException<FamilyCandidateSet>(new DeferredRootBatchFailure(batch, failure));
        });
    internal ValueTask<FamilyCandidateSet> InvokeAsync(IFamilyInvocation family,
        FamilyObservationWindow observation, FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(owner, family) || batch != observation.Batch || input.Batch != batch || !input.IsDense)
            throw new InvalidDataException("PreparedFamilyBatch.OwnerOrBatchMismatch");
        if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("PreparedFamilyBatch.AlreadyConsumed");
        return invoke(token);
    }
}
