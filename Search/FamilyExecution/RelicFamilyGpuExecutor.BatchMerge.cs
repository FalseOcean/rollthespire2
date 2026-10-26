using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// K 個壓縮 Relic 窗口共用一次 Submit/Sync，標頭合併讀回。
// 每個窗口只配置有界輸入輸出，避免複製 K 份 64 MiB 工作區。
internal sealed partial class RelicFamilyGpuExecutor
{
    // 超出壓縮容量或使用稠密輸入時，退回逐窗口執行。
    internal const int BatchCompactCapacity = 1 << 16;

    private Rid[] _bmMeta = [];
    private Rid[] _bmInput = [];
    private Rid[] _bmHeader = [];
    private Rid[] _bmOutput = [];
    private Rid[] _bmPlanMeta = [];
    private Rid[] _bmUniforms = [];
    private Rid _bmHeaderBank;
    private int _bmCapacity;
    private bool _bmResourcesUnavailable;

    internal sealed class RelicBatchSubmission
    {
        internal required IReadOnlyList<FamilyCandidateSet> Inputs { get; init; }
        internal required int[] InputCounts { get; init; }
        internal required uint[] Groups { get; init; }
        internal required double MetadataTotalMs { get; init; }
        internal required double[] PackAllocationMs { get; init; }
        internal required double[] PackConvertMs { get; init; }
        internal required long[] PackAllocatedBytes { get; init; }
        internal required double[] StagingMs { get; init; }
        internal required long[] StagingAllocatedBytes { get; init; }
        internal required double[] BufferUpdateMs { get; init; }
        internal double CommandMs { get; set; }
    }

    internal bool SupportsBatchExecution =>
        FamilyBatchGrouping.Enabled && _privateInput is null && _privateOutput is null;

    internal static bool CanBatchInput(FamilyCandidateSet input) =>
        !input.IsDense && input.Count > 0 && input.Count <= BatchCompactCapacity;

    private Rid AddBatchOwned(Rid rid) { _owned.Add(rid); return rid; }

    private void EnsureBatchResources()
    {
        if (_bmCapacity > 0) return;
        if (_bmResourcesUnavailable) throw new FamilyBatchNotSupportedException("R.BatchMergeResourcesUnavailable");
        int capacity = FamilyBatchGrouping.GroupSize;
        if (capacity < 2) throw new FamilyBatchNotSupportedException("R.BatchMergeDisabled");
        int ownedBefore = _owned.Count;
        try
        {
            int headerBytes = ActiveHeaderUIntCount * sizeof(uint);
            int compactBytes = BatchCompactCapacity * sizeof(uint);
            _bmMeta = new Rid[capacity];
            _bmInput = new Rid[capacity];
            _bmHeader = new Rid[capacity];
            _bmOutput = new Rid[capacity];
            _bmPlanMeta = new Rid[capacity];
            _bmUniforms = new Rid[capacity];
            // plan_meta[5] 必須等於窗口輸出容量；超限設溢位旗標，由標頭驗證拒絕結果。
            uint[] windowPlanMeta = (uint[])_planMetaRaw.Clone();
            windowPlanMeta[5] = BatchCompactCapacity;
            for (int index = 0; index < capacity; index++)
            {
                _bmMeta[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                    _rd, 6 * sizeof(uint), $"R.BatchMergeMeta{index}"));
                _bmInput[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                    _rd, compactBytes, $"R.BatchMergeInput{index}"));
                _bmHeader[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateStorageBuffer(
                    _rd, FamilyGpuComputeUtility.ToBytes(CreateHeader(_planTag, _bonesKBoundary)),
                    $"R.BatchMergeHeader{index}"));
                _bmOutput[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                    _rd, compactBytes, $"R.BatchMergeOutput{index}"));
                _bmPlanMeta[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateStorageBuffer(
                    _rd, FamilyGpuComputeUtility.ToBytes(windowPlanMeta), $"R.BatchMergePlanMeta{index}"));
            }
            _bmHeaderBank = AddBatchOwned(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                _rd, capacity * headerBytes, "R.BatchMergeHeaderBank"));
            for (int index = 0; index < capacity; index++)
            {
                // 沿用單窗口綁定順序；各窗口的 plan_meta 只調整有界輸出容量。
                Rid[] buffers = (Rid[])_bindingBuffers.Clone();
                buffers[0] = _bmMeta[index];
                buffers[1] = _bmInput[index];
                buffers[5] = _bmPlanMeta[index];
                buffers[9] = _bmHeader[index];
                buffers[10] = _bmOutput[index];
                _bmUniforms[index] = AddBatchOwned(FamilyGpuComputeUtility.CreateUniformSet(
                    _rd, _shader, buffers, $"R.BatchMerge{index}"));
            }
            _bmCapacity = capacity;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _bmResourcesUnavailable = true;
            int added = _owned.Count - ownedBefore;
            if (added > 0)
            {
                var partial = _owned.GetRange(ownedBefore, added);
                _owned.RemoveRange(ownedBefore, added);
                try { FamilyGpuComputeUtility.FreeAll(_rd, partial); }
                catch { /* 最後仍由 Dispose 清理。 */ }
            }
            _bmMeta = []; _bmInput = []; _bmHeader = []; _bmOutput = [];
            _bmPlanMeta = []; _bmUniforms = []; _bmCapacity = 0;
            throw new FamilyBatchNotSupportedException("R.BatchMergeResourcesUnavailable:" + ex.Message, ex);
        }
    }

    internal (FamilyCandidateSet[] Results, RelicFamilyGpuBatchMetrics[] Metrics) ExecuteBatch(
        IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken cancellationToken)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportsBatchExecution) throw new FamilyBatchNotSupportedException("R.BatchMergeUnsupported");
        int count = inputs.Count;
        if (count is 0 or 1) throw new InvalidOperationException("R.BatchMergeGroupSize");
        if (count > FamilyBatchGrouping.GroupSize) throw new InvalidOperationException("R.BatchMergeGroupTooLarge");
        var inputCounts = new int[count];
        for (int index = 0; index < count; index++)
        {
            if (!CanBatchInput(inputs[index])) throw new FamilyBatchNotSupportedException("R.BatchMergeInputUnsupported");
            inputCounts[index] = inputs[index].Count;
        }
        EnsureBatchResources();

        var canonicalTimer = Stopwatch.StartNew();
        var timer = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            SearchBatch batch = inputs[index].Batch;
            FamilyGpuComputeUtility.Update(_rd, _bmMeta[index],
            [
                unchecked((uint)batch.BatchBase), unchecked((uint)(batch.BatchBase >> 32)),
                checked((uint)batch.BatchCandidateCount), checked((uint)inputCounts[index]),
                1u, OutputCapacity
            ], "R.BatchMergeMeta");
            FamilyGpuComputeUtility.Update(_rd, _bmHeader[index],
                CreateHeader(_planTag, _bonesKBoundary), "R.BatchMergeHeader");
        }
        double metadataTotalMs = timer.Elapsed.TotalMilliseconds;

        var packAllocationMs = new double[count];
        var packConvertMs = new double[count];
        var packAllocatedBytes = new long[count];
        var stagingMs = new double[count];
        var stagingAllocatedBytes = new long[count];
        var bufferUpdateMs = new double[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<ulong> ordinals = inputs[index].ExportAbi1().Span;
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            var packed = new uint[ordinals.Length];
            packAllocationMs[index] = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            for (int ordinal = 0; ordinal < ordinals.Length; ordinal++)
                packed[ordinal] = checked((uint)ordinals[ordinal]);
            packConvertMs[index] = timer.Elapsed.TotalMilliseconds;
            packAllocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            byte[] packedBytes = FamilyGpuComputeUtility.ToBytes(packed);
            stagingMs[index] = timer.Elapsed.TotalMilliseconds;
            stagingAllocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            timer.Restart();
            FamilyGpuComputeUtility.UpdateBytes(_rd, _bmInput[index], packedBytes, "R.BatchMergeInput");
            bufferUpdateMs[index] = timer.Elapsed.TotalMilliseconds;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int candidatesPerGroup = WorkgroupSize * SeedsPerInvocation;
        var groups = new uint[count];
        timer.Restart();
        long list = _rd.ComputeListBegin();
        try
        {
            for (int index = 0; index < count; index++)
            {
                groups[index] = checked((uint)((inputCounts[index] + candidatesPerGroup - 1) / candidatesPerGroup));
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                _rd.ComputeListBindUniformSet(list, _bmUniforms[index], 0);
                _rd.ComputeListDispatch(list, groups[index], 1u, 1u);
            }
        }
        finally { _rd.ComputeListEnd(); }
        double commandMs = timer.Elapsed.TotalMilliseconds;
        (double submitMs, double syncMs) = FamilyGpuComputeUtility.SubmitAndSync(_rd, "R.BatchMerge");
        cancellationToken.ThrowIfCancellationRequested();

        int headerBytes = ActiveHeaderUIntCount * sizeof(uint);
        for (int index = 0; index < count; index++)
        {
            if (_rd.BufferCopy(_bmHeader[index], _bmHeaderBank, 0,
                    checked((uint)(index * headerBytes)), checked((uint)headerBytes)) != Error.Ok)
                throw new InvalidOperationException("R.BatchMergeHeaderCopyFailed");
        }
        timer.Restart();
        byte[] rawHeaders = _rd.BufferGetData(_bmHeaderBank, 0, checked((uint)(count * headerBytes)));
        if (rawHeaders.Length != count * headerBytes)
            throw new InvalidDataException(
                $"R.BatchMergeHeaderLengthMismatch:expected={count * headerBytes};actual={rawHeaders.Length}");
        double headerReadTotalMs = timer.Elapsed.TotalMilliseconds;

        var results = new FamilyCandidateSet[count];
        var processedCounts = new int[count];
        var survivorCounts = new int[count];
        var payloadBytes = new long[count];
        var payloadReadMs = new double[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> slice = rawHeaders.AsSpan(index * headerBytes, headerBytes);
            uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(slice.ToArray());
            int processedCount = ValidateHeader(header, inputCounts[index]);
            processedCounts[index] = processedCount;
            if (_bonesKBoundary &&
                (header[10] > (uint)inputCounts[index] || header[11] > (uint)inputCounts[index] ||
                 header[6] > header[11] || header[7] > header[10] ||
                 header[6] > (uint)inputCounts[index] || header[7] > (uint)inputCounts[index] ||
                 header[8] > header[6] || header[9] > header[7] ||
                 header[2] < Math.Max(header[8], header[9]) ||
                 header[2] > (ulong)header[8] + header[9]))
                throw new InvalidDataException("R.BatchMergeBonesBoundaryRouteCountersInvalid");
            int survivorCount = checked((int)header[2]);
            survivorCounts[index] = survivorCount;
            if (index == count - 1)
            {
                // 診斷計數器保留最後一個窗口的值。
                LastEarlyHeavy = header[6]; LastLateHeavy = header[7];
                if (_bonesKBoundary)
                {
                    LastEarlyPass = header[8]; LastLatePass = header[9];
                    LastRouteA = header[10]; LastRouteB = header[11];
                }
            }
            timer.Restart();
            byte[] rawOrdinals = survivorCount == 0
                ? []
                : _rd.BufferGetData(_bmOutput[index], 0, checked((uint)(survivorCount * sizeof(uint))));
            payloadReadMs[index] = timer.Elapsed.TotalMilliseconds;
            if (rawOrdinals.Length != survivorCount * sizeof(uint))
                throw new InvalidDataException("R.BatchMergePayloadLengthMismatch");
            payloadBytes[index] = rawOrdinals.LongLength;
            ulong[] survivors = FamilyGpuComputeUtility.FromUInt32Bytes(rawOrdinals)
                .Select(value => (ulong)value).ToArray();
            if (survivors.Length > 1) Array.Sort(survivors);
            results[index] = FamilyCandidateSet.FromSortedAbi1(inputs[index].Batch, survivors);
        }
        canonicalTimer.Stop();
        double canonicalTotalMs = canonicalTimer.Elapsed.TotalMilliseconds;
        var metrics = new RelicFamilyGpuBatchMetrics[count];
        for (int index = 0; index < count; index++)
        {
            metrics[index] = new RelicFamilyGpuBatchMetrics(
                inputCounts[index], processedCounts[index], survivorCounts[index], groups[index],
                metadataTotalMs / count,
                packAllocationMs[index], packConvertMs[index], packAllocatedBytes[index],
                stagingMs[index], stagingAllocatedBytes[index], bufferUpdateMs[index],
                commandMs / count, submitMs / count, syncMs / count,
                headerReadTotalMs / count + payloadReadMs[index], headerBytes + payloadBytes[index],
                canonicalTotalMs / count);
        }
        return (results, metrics);
    }

    // 將 K 個壓縮窗口記入共用 epoch；本處不 Submit/Sync，完成後再收取結果。
    internal RelicBatchSubmission PrepareEpochBatch(FamilyEpoch epoch,
        IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(epoch.Device, _rd)) throw new InvalidOperationException("R.EpochDeviceMismatch");
        if (!SupportsBatchExecution) throw new FamilyBatchNotSupportedException("R.BatchMergeUnsupported");
        int count = inputs.Count;
        if (count is 0 or 1) throw new InvalidOperationException("R.BatchMergeGroupSize");
        if (count > FamilyBatchGrouping.GroupSize) throw new InvalidOperationException("R.BatchMergeGroupTooLarge");
        var inputCounts = new int[count];
        for (int index = 0; index < count; index++)
        {
            if (!CanBatchInput(inputs[index]))
                throw new FamilyBatchNotSupportedException("R.BatchMergeInputUnsupported");
            inputCounts[index] = inputs[index].Count;
        }
        EnsureBatchResources();

        var timer = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            SearchBatch batch = inputs[index].Batch;
            FamilyGpuComputeUtility.Update(_rd, _bmMeta[index],
            [
                unchecked((uint)batch.BatchBase), unchecked((uint)(batch.BatchBase >> 32)),
                checked((uint)batch.BatchCandidateCount), checked((uint)inputCounts[index]),
                1u, OutputCapacity
            ], "R.EpochMeta");
            FamilyGpuComputeUtility.Update(_rd, _bmHeader[index],
                CreateHeader(_planTag, _bonesKBoundary), "R.EpochHeader");
        }
        double metadataTotalMs = timer.Elapsed.TotalMilliseconds;

        var packAllocationMs = new double[count];
        var packConvertMs = new double[count];
        var packAllocatedBytes = new long[count];
        var stagingMs = new double[count];
        var stagingAllocatedBytes = new long[count];
        var bufferUpdateMs = new double[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<ulong> ordinals = inputs[index].ExportAbi1().Span;
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            var packed = new uint[ordinals.Length];
            packAllocationMs[index] = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            for (int ordinal = 0; ordinal < ordinals.Length; ordinal++)
                packed[ordinal] = checked((uint)ordinals[ordinal]);
            packConvertMs[index] = timer.Elapsed.TotalMilliseconds;
            packAllocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            byte[] packedBytes = FamilyGpuComputeUtility.ToBytes(packed);
            stagingMs[index] = timer.Elapsed.TotalMilliseconds;
            stagingAllocatedBytes[index] = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            timer.Restart();
            FamilyGpuComputeUtility.UpdateBytes(_rd, _bmInput[index], packedBytes, "R.EpochInput");
            bufferUpdateMs[index] = timer.Elapsed.TotalMilliseconds;
        }

        int candidatesPerGroup = WorkgroupSize * SeedsPerInvocation;
        var groups = new uint[count];
        for (int index = 0; index < count; index++)
            groups[index] = checked((uint)((inputCounts[index] + candidatesPerGroup - 1) / candidatesPerGroup));
        var submission = new RelicBatchSubmission
        {
            Inputs = inputs,
            InputCounts = inputCounts,
            Groups = groups,
            MetadataTotalMs = metadataTotalMs,
            PackAllocationMs = packAllocationMs,
            PackConvertMs = packConvertMs,
            PackAllocatedBytes = packAllocatedBytes,
            StagingMs = stagingMs,
            StagingAllocatedBytes = stagingAllocatedBytes,
            BufferUpdateMs = bufferUpdateMs,
            CommandMs = 0d
        };
        _pendingEpochSubmission = submission;
        return submission;
    }

    private RelicBatchSubmission? _pendingEpochSubmission;

    internal void RecordEpochDispatches(FamilyEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (!ReferenceEquals(epoch.Device, _rd)) throw new InvalidOperationException("R.EpochDeviceMismatch");
        RelicBatchSubmission submission = _pendingEpochSubmission ??
            throw new InvalidOperationException("R.EpochNoPreparedSubmission");
        var timer = Stopwatch.StartNew();
        long list = epoch.List;
        for (int index = 0; index < submission.InputCounts.Length; index++)
        {
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, _bmUniforms[index], 0);
            _rd.ComputeListDispatch(list, submission.Groups[index], 1u, 1u);
        }
        submission.CommandMs = timer.Elapsed.TotalMilliseconds;
        epoch.MarkFamily();
    }

    // 共用 epoch 同步後收取 Relic 結果；各窗口直接讀取小型標頭。
    internal (FamilyCandidateSet[] Results, RelicFamilyGpuBatchMetrics[] Metrics) CompleteEpochBatch(
        RelicBatchSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pendingEpochSubmission = null;
        ArgumentNullException.ThrowIfNull(submission);
        AssertOwnerThread();
        tokenThrow(cancellationToken);
        int count = submission.Inputs.Count;
        int[] inputCounts = submission.InputCounts;
        var canonicalTimer = Stopwatch.StartNew();
        var timer = new Stopwatch();
        int headerBytes = ActiveHeaderUIntCount * sizeof(uint);
        var headers = new uint[count][];
        var survivorCounts = new int[count];
        var processedCounts = new int[count];
        var payloadBytes = new long[count];
        var payloadReadMs = new double[count];
        double headerReadTotalMs = 0d;
        for (int index = 0; index < count; index++)
        {
            timer.Restart();
            byte[] rawHeader = _rd.BufferGetData(_bmHeader[index], 0, checked((uint)headerBytes));
            headerReadTotalMs += timer.Elapsed.TotalMilliseconds;
            uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(rawHeader);
            int processedCount = ValidateHeader(header, inputCounts[index]);
            processedCounts[index] = processedCount;
            if (_bonesKBoundary &&
                (header[10] > (uint)inputCounts[index] || header[11] > (uint)inputCounts[index] ||
                 header[6] > header[11] || header[7] > header[10] ||
                 header[6] > (uint)inputCounts[index] || header[7] > (uint)inputCounts[index] ||
                 header[8] > header[6] || header[9] > header[7] ||
                 header[2] < Math.Max(header[8], header[9]) ||
                 header[2] > (ulong)header[8] + header[9]))
                throw new InvalidDataException("R.EpochBonesBoundaryRouteCountersInvalid");
            headers[index] = header;
            survivorCounts[index] = checked((int)header[2]);
            if (index == count - 1)
            {
                LastEarlyHeavy = header[6]; LastLateHeavy = header[7];
                if (_bonesKBoundary)
                {
                    LastEarlyPass = header[8]; LastLatePass = header[9];
                    LastRouteA = header[10]; LastRouteB = header[11];
                }
            }
        }
        var results = new FamilyCandidateSet[count];
        for (int index = 0; index < count; index++)
        {
            int survivorCount = survivorCounts[index];
            timer.Restart();
            byte[] rawOrdinals = survivorCount == 0
                ? []
                : _rd.BufferGetData(_bmOutput[index], 0, checked((uint)(survivorCount * sizeof(uint))));
            payloadReadMs[index] = timer.Elapsed.TotalMilliseconds;
            if (rawOrdinals.Length != survivorCount * sizeof(uint))
                throw new InvalidDataException("R.EpochPayloadLengthMismatch");
            payloadBytes[index] = rawOrdinals.LongLength;
            ulong[] survivors = FamilyGpuComputeUtility.FromUInt32Bytes(rawOrdinals)
                .Select(value => (ulong)value).ToArray();
            if (survivors.Length > 1) Array.Sort(survivors);
            results[index] = FamilyCandidateSet.FromSortedAbi1(submission.Inputs[index].Batch, survivors);
        }
        canonicalTimer.Stop();
        double canonicalTotalMs = submission.MetadataTotalMs + submission.CommandMs +
                                  canonicalTimer.Elapsed.TotalMilliseconds;
        var metrics = new RelicFamilyGpuBatchMetrics[count];
        for (int index = 0; index < count; index++)
        {
            metrics[index] = new RelicFamilyGpuBatchMetrics(
                inputCounts[index], processedCounts[index], survivorCounts[index], submission.Groups[index],
                submission.MetadataTotalMs / count,
                submission.PackAllocationMs[index], submission.PackConvertMs[index], submission.PackAllocatedBytes[index],
                submission.StagingMs[index], submission.StagingAllocatedBytes[index], submission.BufferUpdateMs[index],
                submission.CommandMs / count, 0d, 0d,
                headerReadTotalMs / count + payloadReadMs[index], headerBytes + payloadBytes[index],
                canonicalTotalMs / count);
        }
        return (results, metrics);
    }

    private static void tokenThrow(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
    }


    internal void AbortPendingBatch()
    {
        // 此工作沒有獨立提交，Submit/Sync 已由共用 epoch 負責。
        _pendingEpochSubmission = null;
    }
}
