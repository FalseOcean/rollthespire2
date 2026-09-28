using System.Diagnostics;
using System.Globalization;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Opt-in, batch-aggregate observer for the current host-resident S-to-R transport.
/// It does not publish Family performance evidence or influence execution.
/// </summary>
internal sealed class FamilyPipelineBoundaryAudit
{
    private const string EnabledVariable = "RT2_FAMILY_PIPELINE_BOUNDARY_AUDIT";
    private const int InitialSamples = 8;
    private const int SamplePeriod = 64;
    private readonly object _gate = new();
    private SourceObservation? _pending;
    private long _observations;
    private long _steadyObservations;
    private long _inputCandidates;
    private long _survivors;
    private long _payloadBytes;
    private double _sourcePhysicalMs;
    private double _sourceSubmitMs;
    private double _sourceSyncMs;
    private double _headerReadbackMs;
    private double _payloadReadbackMs;
    private long _payloadReadbackAllocatedBytes;
    private double _sourceMaterializeMs;
    private double _sourceTypedAllocationMs;
    private double _sourceDecodePayloadMs;
    private double _sourceSortMs;
    private double _sourceValidationCandidateSetMs;
    private double _sourceCanonicalAbi1ReadyMs;
    private double _stableCountsReadbackMs;
    private double _stableHostPrefixMs;
    private double _stableOffsetsUploadMs;
    private double _stablePhase1GpuBoundaryMs;
    private double _stableScatterGpuBoundaryMs;
    private long _stableObservations;
    private long _sourceHostAllocatedBytes;
    private double _ownerHandoffMs;
    private double _targetPackMs;
    private double _targetPackAllocationMs;
    private double _targetPackConvertValidateMs;
    private long _targetPackAllocatedBytes;
    private double _targetMetadataUploadMs;
    private double _targetPayloadUploadMs;
    private double _targetUploadStagingMs;
    private long _targetUploadStagingAllocatedBytes;
    private double _targetBufferUpdateMs;
    private double _targetDispatchBoundaryMs;
    private double _targetPhysicalMs;
    private double _boundaryTotalMs;

    public static FamilyPipelineBoundaryAudit? TryCreate() =>
        string.Equals(System.Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal)
            ? new FamilyPipelineBoundaryAudit()
            : null;

    public void RecordSource(
        SearchBatch batch,
        MerchantShopColorlessGpuBatchMetrics metrics,
        long ownerCompletionTimestamp,
        int sourceBatchNumber)
    {
        lock (_gate)
        {
            _pending = new SourceObservation(batch, metrics, ownerCompletionTimestamp, sourceBatchNumber);
        }
    }

    public void CompleteTarget(
        SearchBatch batch,
        RelicFamilyGpuBatchMetrics target,
        long targetOwnerStartTimestamp,
        bool targetDispatched)
    {
        SourceObservation source;
        lock (_gate)
        {
            if (_pending is not SourceObservation pending || pending.Batch != batch) return;
            source = pending;
            _pending = null;
        }

        double ownerHandoffMs = targetOwnerStartTimestamp <= source.OwnerCompletionTimestamp
            ? 0d
            : Stopwatch.GetElapsedTime(source.OwnerCompletionTimestamp, targetOwnerStartTimestamp).TotalMilliseconds;
        double targetDispatchBoundaryMs = target.CommandMs + target.SubmitMs;
        double boundaryTotalMs =
            source.Metrics.SubmitMs +
            source.Metrics.SyncMs +
            source.Metrics.HeaderReadbackMs +
            source.Metrics.PayloadReadbackMs +
            source.Metrics.StableCountsReadbackMs +
            source.Metrics.StableHostPrefixMs +
            source.Metrics.StableOffsetsUploadMs +
            source.Metrics.HostMaterializeMs +
            ownerHandoffMs +
            target.CompactPackMs +
            target.MetadataUploadMs +
            target.CompactUploadMs +
            targetDispatchBoundaryMs;
        long observationNumber;
        lock (_gate)
        {
            observationNumber = ++_observations;
            if (source.SourceBatchNumber > 1)
            {
                _steadyObservations++;
                _inputCandidates += source.Metrics.InputCandidates;
                _survivors += source.Metrics.Survivors;
                _payloadBytes += source.Metrics.PayloadReadbackBytes;
                _sourcePhysicalMs += source.Metrics.PhysicalMs;
                _sourceSubmitMs += source.Metrics.SubmitMs;
                _sourceSyncMs += source.Metrics.SyncMs;
                _headerReadbackMs += source.Metrics.HeaderReadbackMs;
                _payloadReadbackMs += source.Metrics.PayloadReadbackMs;
                _payloadReadbackAllocatedBytes += source.Metrics.PayloadReadbackAllocatedBytes;
                _sourceMaterializeMs += source.Metrics.HostMaterializeMs;
                _sourceTypedAllocationMs += source.Metrics.TypedAllocationMs;
                _sourceDecodePayloadMs += source.Metrics.DecodePayloadMs;
                _sourceSortMs += source.Metrics.SortMs;
                _sourceValidationCandidateSetMs += source.Metrics.ValidationCandidateSetMs;
                _sourceCanonicalAbi1ReadyMs += source.Metrics.CanonicalAbi1ReadyMs;
                _stableCountsReadbackMs += source.Metrics.StableCountsReadbackMs;
                _stableHostPrefixMs += source.Metrics.StableHostPrefixMs;
                _stableOffsetsUploadMs += source.Metrics.StableOffsetsUploadMs;
                _stablePhase1GpuBoundaryMs += source.Metrics.StablePhase1CommandMs + source.Metrics.StablePhase1SubmitMs + source.Metrics.StablePhase1SyncMs;
                _stableScatterGpuBoundaryMs += source.Metrics.StableScatterCommandMs + source.Metrics.StableScatterSubmitMs + source.Metrics.StableScatterSyncMs;
                if (source.Metrics.CompactionPath == MerchantShopColorlessCompactionPath.StableOrderedCompaction)
                    _stableObservations++;
                _sourceHostAllocatedBytes += source.Metrics.HostAllocatedBytes;
                _ownerHandoffMs += ownerHandoffMs;
                _targetPackMs += target.CompactPackMs;
                _targetPackAllocationMs += target.CompactPackAllocationMs;
                _targetPackConvertValidateMs += target.CompactPackConvertValidateMs;
                _targetPackAllocatedBytes += target.CompactPackAllocatedBytes;
                _targetMetadataUploadMs += target.MetadataUploadMs;
                _targetPayloadUploadMs += target.CompactUploadMs;
                _targetUploadStagingMs += target.CompactUploadStagingMs;
                _targetUploadStagingAllocatedBytes += target.CompactUploadStagingAllocatedBytes;
                _targetBufferUpdateMs += target.CompactBufferUpdateMs;
                _targetDispatchBoundaryMs += targetDispatchBoundaryMs;
                _targetPhysicalMs += target.PhysicalMs;
                _boundaryTotalMs += boundaryTotalMs;
            }
        }

        if (observationNumber <= InitialSamples || observationNumber % SamplePeriod == 0)
        {
            double computeMs = source.Metrics.PhysicalMs + target.PhysicalMs;
            long typedOrdinalBytes = checked((long)source.Metrics.Survivors * sizeof(ulong));
            long packBytes = targetDispatched ? checked((long)target.InputCandidates * sizeof(uint)) : 0L;
            long estimatedTemporaryBytes = checked(source.Metrics.PayloadReadbackBytes + typedOrdinalBytes + packBytes + packBytes);
            RuntimeLog.TryBackgroundInfo(
                "familyPipelineBoundary=true;from=S.MerchantShopColorless;to=R.Relic;" +
                $"observationNumber={observationNumber};sourceBatchNumber={source.SourceBatchNumber};" +
                $"steady={(source.SourceBatchNumber > 1).ToString().ToLowerInvariant()};targetDispatched={targetDispatched.ToString().ToLowerInvariant()};" +
                $"inputCount={source.Metrics.InputCandidates};survivorCount={source.Metrics.Survivors};" +
                $"sourceCompactionPath={source.Metrics.CompactionPath};canonicalAbi1ReadyMs={F(source.Metrics.CanonicalAbi1ReadyMs)};" +
                $"payloadBytes={source.Metrics.PayloadReadbackBytes};" +
                $"sourceGpuMs={F(source.Metrics.PhysicalMs)};sourceSubmitMs={F(source.Metrics.SubmitMs)};" +
                $"sourceSyncWaitMs={F(source.Metrics.SyncMs)};headerWaitReadbackMs={F(source.Metrics.HeaderReadbackMs)};" +
                $"payloadReadbackMs={F(source.Metrics.PayloadReadbackMs)};payloadReadbackMiBPerSecond={F(RateMiB(source.Metrics.PayloadReadbackBytes, source.Metrics.PayloadReadbackMs))};" +
                $"payloadReadbackAllocatedBytes={source.Metrics.PayloadReadbackAllocatedBytes};" +
                $"sourceHostMaterializeMs={F(source.Metrics.HostMaterializeMs)};" +
                $"sourceTypedAllocationMs={F(source.Metrics.TypedAllocationMs)};sourceDecodePayloadMs={F(source.Metrics.DecodePayloadMs)};" +
                $"sourceSortMs={F(source.Metrics.SortMs)};sourceValidationCandidateSetMs={F(source.Metrics.ValidationCandidateSetMs)};" +
                $"stablePhase1GpuBoundaryMs={F(source.Metrics.StablePhase1CommandMs + source.Metrics.StablePhase1SubmitMs + source.Metrics.StablePhase1SyncMs)};" +
                $"stableCountsReadbackMs={F(source.Metrics.StableCountsReadbackMs)};stableHostPrefixMs={F(source.Metrics.StableHostPrefixMs)};" +
                $"stableOffsetsUploadMs={F(source.Metrics.StableOffsetsUploadMs)};" +
                $"stableScatterGpuBoundaryMs={F(source.Metrics.StableScatterCommandMs + source.Metrics.StableScatterSubmitMs + source.Metrics.StableScatterSyncMs)};" +
                $"sourceHostAllocatedBytes={source.Metrics.HostAllocatedBytes};typedOrdinalBytes={typedOrdinalBytes};" +
                $"ownerHandoffMs={F(ownerHandoffMs)};targetHostPackMs={F(target.CompactPackMs)};" +
                $"hostPackAllocateMs={F(target.CompactPackAllocationMs)};hostPackConvertValidateMs={F(target.CompactPackConvertValidateMs)};" +
                $"hostPackAllocatedBytes={target.CompactPackAllocatedBytes};packBytes={packBytes};estimatedTemporaryBytes={estimatedTemporaryBytes};" +
                $"targetMetadataUploadMs={F(target.MetadataUploadMs)};targetUploadMs={F(target.CompactUploadMs)};" +
                $"targetUploadStagingMs={F(target.CompactUploadStagingMs)};targetUploadStagingAllocatedBytes={target.CompactUploadStagingAllocatedBytes};" +
                $"targetBufferUpdateMs={F(target.CompactBufferUpdateMs)};targetUploadBytes={(targetDispatched ? checked((long)target.InputCandidates * sizeof(uint)) : 0L)};" +
                $"targetDispatchBoundaryMs={F(targetDispatchBoundaryMs)};targetGpuMs={F(target.PhysicalMs)};" +
                $"boundaryTotalMs={F(boundaryTotalMs)};boundaryOverSourceGpu={F(Ratio(boundaryTotalMs, source.Metrics.PhysicalMs))};" +
                $"boundaryOverTargetGpu={F(Ratio(boundaryTotalMs, target.PhysicalMs))};boundaryOverComputeRatio={F(Ratio(boundaryTotalMs, computeMs))};" +
                "gpuMsDefinition=FamilyPhysicalMs_NoGpuTimestamp;dispatchBoundaryDefinition=CommandRecordPlusSubmit;" +
                "sampling=First8ThenEvery64;hardwarePerformanceEvidence=false");
        }
    }

    public void WriteSummary()
    {
        lock (_gate)
        {
            if (_observations == 0) return;
            double computeMs = _sourcePhysicalMs + _targetPhysicalMs;
            long typedOrdinalBytes = checked(_survivors * sizeof(ulong));
            long packBytes = checked(_survivors * sizeof(uint));
            long estimatedTemporaryBytes = checked(_payloadBytes + typedOrdinalBytes + packBytes + packBytes);
            RuntimeLog.TryBackgroundInfo(
                "familyPipelineBoundarySummary=true;from=S.MerchantShopColorless;to=R.Relic;" +
                $"observations={_observations};steadyObservations={_steadyObservations};" +
                $"stableObservations={_stableObservations};" +
                $"inputCount={_inputCandidates};survivorCount={_survivors};payloadBytes={_payloadBytes};" +
                $"typedOrdinalBytes={typedOrdinalBytes};packBytes={packBytes};estimatedTemporaryBytes={estimatedTemporaryBytes};" +
                $"sourceGpuMs={F(_sourcePhysicalMs)};sourceSubmitMs={F(_sourceSubmitMs)};sourceSyncWaitMs={F(_sourceSyncMs)};" +
                $"headerWaitReadbackMs={F(_headerReadbackMs)};payloadReadbackMs={F(_payloadReadbackMs)};" +
                $"payloadReadbackAllocatedBytes={_payloadReadbackAllocatedBytes};" +
                $"sourceHostMaterializeMs={F(_sourceMaterializeMs)};sourceTypedAllocationMs={F(_sourceTypedAllocationMs)};" +
                $"sourceDecodePayloadMs={F(_sourceDecodePayloadMs)};sourceSortMs={F(_sourceSortMs)};" +
                $"sourceValidationCandidateSetMs={F(_sourceValidationCandidateSetMs)};sourceHostAllocatedBytes={_sourceHostAllocatedBytes};" +
                $"canonicalAbi1ReadyMs={F(_sourceCanonicalAbi1ReadyMs)};" +
                $"canonicalAbi1ReadyCandidatesPerSecond={F(RateCandidates(_inputCandidates, _sourceCanonicalAbi1ReadyMs))};" +
                $"stablePhase1GpuBoundaryMs={F(_stablePhase1GpuBoundaryMs)};stableCountsReadbackMs={F(_stableCountsReadbackMs)};" +
                $"stableHostPrefixMs={F(_stableHostPrefixMs)};stableOffsetsUploadMs={F(_stableOffsetsUploadMs)};" +
                $"stableScatterGpuBoundaryMs={F(_stableScatterGpuBoundaryMs)};" +
                $"ownerHandoffMs={F(_ownerHandoffMs)};targetHostPackMs={F(_targetPackMs)};" +
                $"hostPackAllocateMs={F(_targetPackAllocationMs)};hostPackConvertValidateMs={F(_targetPackConvertValidateMs)};" +
                $"hostPackAllocatedBytes={_targetPackAllocatedBytes};targetMetadataUploadMs={F(_targetMetadataUploadMs)};" +
                $"targetUploadMs={F(_targetPayloadUploadMs)};targetUploadStagingMs={F(_targetUploadStagingMs)};" +
                $"targetUploadStagingAllocatedBytes={_targetUploadStagingAllocatedBytes};targetBufferUpdateMs={F(_targetBufferUpdateMs)};" +
                $"targetDispatchBoundaryMs={F(_targetDispatchBoundaryMs)};" +
                $"targetGpuMs={F(_targetPhysicalMs)};boundaryTotalMs={F(_boundaryTotalMs)};" +
                $"boundaryOverSourceGpu={F(Ratio(_boundaryTotalMs, _sourcePhysicalMs))};" +
                $"boundaryOverTargetGpu={F(Ratio(_boundaryTotalMs, _targetPhysicalMs))};" +
                $"boundaryOverComputeRatio={F(Ratio(_boundaryTotalMs, computeMs))};" +
                "firstSourceBatchExcludedFromTotals=true;hardwarePerformanceEvidence=false");
        }
    }

    private static double Ratio(double numerator, double denominator) => denominator <= 0d ? 0d : numerator / denominator;
    private static double RateMiB(long bytes, double milliseconds) => milliseconds <= 0d ? 0d : bytes * 1000d / milliseconds / (1024d * 1024d);
    private static double RateCandidates(long candidates, double milliseconds) => milliseconds <= 0d ? 0d : candidates * 1000d / milliseconds;
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private readonly record struct SourceObservation(
        SearchBatch Batch,
        MerchantShopColorlessGpuBatchMetrics Metrics,
        long OwnerCompletionTimestamp,
        int SourceBatchNumber);
}
