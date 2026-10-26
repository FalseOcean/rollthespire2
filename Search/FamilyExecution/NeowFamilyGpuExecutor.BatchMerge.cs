using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// K 個獨立稠密 Neow 窗口共用一次計算清單與 Submit/Sync。
// 各窗口保留自己的標頭與輸出；標頭前綴及中型候選分別合併讀回。
// 僅支援單人公開稠密輸入、合併與中型讀回；其他形狀退回逐窗口。
internal sealed partial class NeowFamilyGpuExecutor
{
    private Rid[] _batchMeta = [];
    private Rid[] _batchHeader = [];
    private Rid[] _batchOutput = [];
    private Rid[] _batchCurse = [];
    private Rid[] _batchPair = [];
    private Rid[][] _batchUniforms = [];
    private Rid _batchHeaderBank;
    private Rid _batchMediumBank;
    private int _batchCapacity;
    private bool _batchResourcesUnavailable;

    internal bool SupportsBatchExecution =>
        FamilyBatchGrouping.Enabled && _useCombinedReadback && _useMediumReadback &&
        !_privateInput && !_privateOutput && _partySource is null && _composite is null &&
        !_bonesArcane && _bonesKMode == 0;

    private void EnsureBatchResources(bool staged)
    {
        if (_batchCapacity > 0) return;
        if (_batchResourcesUnavailable) throw new FamilyBatchNotSupportedException("N.BatchMergeResourcesUnavailable");
        int capacity = FamilyBatchGrouping.GroupSize;
        if (capacity < 2) throw new FamilyBatchNotSupportedException("N.BatchMergeDisabled");
        int ownedBefore = _owned.Count;
        try
        {
            int stageCount = staged ? 3 : 1;
            _batchMeta = new Rid[capacity];
            _batchHeader = new Rid[capacity];
            _batchOutput = new Rid[capacity];
            _batchUniforms = new Rid[capacity][];
            if (staged)
            {
                _batchCurse = new Rid[capacity];
                _batchPair = new Rid[capacity];
            }
            for (int index = 0; index < capacity; index++)
            {
                _batchMeta[index] = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                    _rd, 8 * sizeof(uint), $"N.BatchMergeMeta{index}"));
                _batchHeader[index] = Add(FamilyGpuComputeUtility.CreateStorageBuffer(
                    _rd, FamilyGpuComputeUtility.ToBytesNonEmpty(Header()), $"N.BatchMergeHeader{index}"));
                _batchOutput[index] = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                    _rd, Capacity * sizeof(uint), $"N.BatchMergeOutput{index}"));
                if (staged)
                {
                    _batchCurse[index] = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                        _rd, NeowFamilyGpuPlan.CurseCapacity * 12, $"N.BatchMergeCurse{index}"));
                    _batchPair[index] = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                        _rd, NeowFamilyGpuPlan.PairCapacity * 48, $"N.BatchMergePair{index}"));
                }
            }
            for (int index = 0; index < capacity; index++)
            {
                Rid[] buffers = BatchWindowBuffers(index, staged);
                _batchUniforms[index] = new Rid[stageCount];
                for (int stage = 0; stage < stageCount; stage++)
                {
                    Rid shader = staged ? _stages[stage].Shader : _singleShader;
                    _batchUniforms[index][stage] = Add(FamilyGpuComputeUtility.CreateUniformSet(
                        _rd, shader, buffers, $"N.BatchMerge{index}.S{stage}"));
                }
            }
            _batchHeaderBank = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                _rd, capacity * NeowCombinedReadbackTransport.BankBytes, "N.BatchMergeHeaderBank"));
            _batchMediumBank = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(
                _rd, capacity * NeowMediumReadbackTransport.BankBytes, "N.BatchMergeMediumBank"));
            _batchCapacity = capacity;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 先完成分配再 Dispatch；分配失敗便釋放部分資源，退回逐窗口。
            _batchResourcesUnavailable = true;
            int added = _owned.Count - ownedBefore;
            if (added > 0)
            {
                var partial = _owned.GetRange(ownedBefore, added);
                _owned.RemoveRange(ownedBefore, added);
                try { FamilyGpuComputeUtility.FreeAll(_rd, partial); }
                catch { /* 裝置可能已故障，最後仍由 Dispose 清理。 */ }
            }
            _batchMeta = [];
            _batchHeader = [];
            _batchOutput = [];
            _batchCurse = [];
            _batchPair = [];
            _batchUniforms = [];
            _batchCapacity = 0;
            throw new FamilyBatchNotSupportedException("N.BatchMergeResourcesUnavailable:" + ex.Message, ex);
        }
    }

    private BatchSubmission? _pendingBatch;

    // 已準備但未收取的群組；SubmitBatch 負責提交，CompleteBatch 負責同步與讀回。
    internal sealed class BatchSubmission
    {
        internal required IReadOnlyList<FamilyCandidateSet> Inputs { get; init; }
        internal required int[] InputCounts { get; init; }
        internal required bool Staged { get; init; }
        internal required double UploadMs { get; init; }
        internal required double SubmitMs { get; init; }
        // 共用 epoch 已負責 Submit/Sync，CompleteBatch 不可再次同步。
        internal bool ExternallySynchronized { get; init; }
    }

    internal (FamilyCandidateSet[] Results, NeowFamilyGpuMetrics[] Metrics) ExecuteBatch(
        IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken token)
    {
        BatchSubmission submission = SubmitBatch(inputs, token);
        return CompleteBatch(submission, token);
    }

    private (int[] InputCounts, bool Staged) PrepareSubmitBatch(IReadOnlyList<FamilyCandidateSet> inputs)
    {
        if (!SupportsBatchExecution) throw new FamilyBatchNotSupportedException("N.BatchMergeUnsupported");
        if (_pendingBatch is not null) throw new InvalidOperationException("N.BatchMergePendingSubmission");
        AssertOwner();
        int count = inputs.Count;
        if (count is 0 or 1) throw new InvalidOperationException("N.BatchMergeGroupSize");
        if (count > FamilyBatchGrouping.GroupSize) throw new InvalidOperationException("N.BatchMergeGroupTooLarge");
        bool staged = _plan.UsesStagedDense;
        var inputCounts = new int[count];
        for (int index = 0; index < count; index++)
        {
            FamilyCandidateSet input = inputs[index];
            if (!input.IsDense) throw new FamilyBatchNotSupportedException("N.BatchMergeDenseOnly");
            if (input.Count > Capacity) throw new FamilyBatchNotSupportedException("N.BatchMergeCapacityExceeded");
            inputCounts[index] = input.Count;
        }
        EnsureModules(staged);
        EnsureBatchResources(staged);
        return (inputCounts, staged);
    }

    private double UploadSubmitBatchInputs(IReadOnlyList<FamilyCandidateSet> inputs, int[] inputCounts)
    {
        var uploadTimer = Stopwatch.StartNew();
        for (int index = 0; index < inputs.Count; index++)
        {
            SearchBatch batch = inputs[index].Batch;
            FamilyGpuComputeUtility.Update(_rd, _batchMeta[index],
                [(uint)batch.BatchBase, (uint)(batch.BatchBase >> 32), (uint)batch.BatchCandidateCount,
                 (uint)inputCounts[index], 0u, Capacity,
                 NeowFamilyGpuPlan.CurseCapacity, NeowFamilyGpuPlan.PairCapacity], "N.BatchMergeMeta");
            FamilyGpuComputeUtility.Update(_rd, _batchHeader[index], Header(), "N.BatchMergeHeader");
        }
        return uploadTimer.Elapsed.TotalMilliseconds;
    }

    private void RecordSubmitBatchDispatches(long list, int[] inputCounts, bool staged)
    {
        for (int index = 0; index < inputCounts.Length; index++)
        {
            uint groups = (uint)((inputCounts[index] + 511) / 512);
            if (groups == 0) continue;
            if (staged)
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    if (stage > 0) _rd.ComputeListAddBarrier(list);
                    _rd.ComputeListBindComputePipeline(list, _stages[stage].Pipeline);
                    _rd.ComputeListBindUniformSet(list, _batchUniforms[index][stage], 0);
                    _rd.ComputeListDispatch(list,
                        stage == 0 ? groups : (uint)((stage == 1 ? NeowFamilyGpuPlan.CurseCapacity : NeowFamilyGpuPlan.PairCapacity) / 512),
                        1, 1);
                }
            }
            else
            {
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                _rd.ComputeListBindUniformSet(list, _batchUniforms[index][0], 0);
                _rd.ComputeListDispatch(list, groups, 1, 1);
            }
        }
    }

    private void QueueSubmitBatchReadbackCopies(int[] inputCounts, FamilyEpoch? epoch)
    {
        // 每個窗口的標頭與前綴複製到互不重疊的區段。
        int slotBytes = NeowCombinedReadbackTransport.BankBytes;
        for (int index = 0; index < inputCounts.Length; index++)
        {
            uint slotOffset = (uint)(index * slotBytes);
            uint prefixBytes = checked((uint)Math.Min(inputCounts[index], NeowCombinedReadbackTransport.PrefixCapacity)
                                       * sizeof(uint));
            if (epoch is null)
            {
                if (_rd.BufferCopy(_batchHeader[index], _batchHeaderBank, 0, slotOffset,
                        NeowCombinedReadbackTransport.HeaderBytes) != Error.Ok)
                    throw new InvalidOperationException("N.BatchMergeHeaderCopyFailed");
                if (prefixBytes != 0 && _rd.BufferCopy(_batchOutput[index], _batchHeaderBank, 0,
                        slotOffset + NeowCombinedReadbackTransport.HeaderBytes, prefixBytes) != Error.Ok)
                    throw new InvalidOperationException("N.BatchMergePrefixCopyFailed");
            }
            else
            {
                epoch.QueueCopy(_batchHeader[index], _batchHeaderBank, 0, slotOffset,
                    NeowCombinedReadbackTransport.HeaderBytes);
                if (prefixBytes != 0)
                    epoch.QueueCopy(_batchOutput[index], _batchHeaderBank, 0,
                        slotOffset + NeowCombinedReadbackTransport.HeaderBytes, prefixBytes);
            }
        }
    }

    internal BatchSubmission SubmitBatch(IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (int[] inputCounts, bool staged) = PrepareSubmitBatch(inputs);
        double uploadedMs = UploadSubmitBatchInputs(inputs, inputCounts);
        long list = _rd.ComputeListBegin();
        RecordSubmitBatchDispatches(list, inputCounts, staged);
        _rd.ComputeListEnd();
        QueueSubmitBatchReadbackCopies(inputCounts, epoch: null);
        long submitStarted = Stopwatch.GetTimestamp();
        _rd.Submit();
        double submitMs = Stopwatch.GetElapsedTime(submitStarted).TotalMilliseconds;
        var submission = new BatchSubmission
        {
            Inputs = inputs,
            InputCounts = inputCounts,
            Staged = staged,
            UploadMs = uploadedMs,
            SubmitMs = submitMs
        };
        _pendingBatch = submission;
        return submission;
    }

    // epoch 第一階段先驗證、上傳並排入複製；此時不可有活動計算清單。
    internal BatchSubmission PrepareEpochBatch(FamilyEpoch epoch, IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(epoch.Device, _rd))
            throw new InvalidOperationException("N.EpochDeviceMismatch");
        (int[] inputCounts, bool staged) = PrepareSubmitBatch(inputs);
        double uploadedMs = UploadSubmitBatchInputs(inputs, inputCounts);
        QueueSubmitBatchReadbackCopies(inputCounts, epoch);
        var submission = new BatchSubmission
        {
            Inputs = inputs,
            InputCounts = inputCounts,
            Staged = staged,
            UploadMs = uploadedMs,
            SubmitMs = 0,
            ExternallySynchronized = true
        };
        _pendingBatch = submission;
        return submission;
    }

    // epoch 第二階段記入共用清單。
    internal void RecordEpochDispatches(FamilyEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (!ReferenceEquals(epoch.Device, _rd))
            throw new InvalidOperationException("N.EpochDeviceMismatch");
        BatchSubmission submission = _pendingBatch ??
            throw new InvalidOperationException("N.EpochNoPreparedSubmission");
        RecordSubmitBatchDispatches(epoch.List, submission.InputCounts, submission.Staged);
        epoch.MarkFamily();
    }

    internal bool HasPendingBatch => _pendingBatch is not null;
    internal void ClearPendingBatch() => _pendingBatch = null;

    internal (FamilyCandidateSet[] Results, NeowFamilyGpuMetrics[] Metrics) CompleteBatch(
        BatchSubmission submission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(submission);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_pendingBatch, submission))
            throw new InvalidOperationException("N.BatchMergeSubmissionMismatch");
        AssertOwner();
        IReadOnlyList<FamilyCandidateSet> inputs = submission.Inputs;
        int count = inputs.Count;
        int[] inputCounts = submission.InputCounts;
        bool staged = submission.Staged;
        double uploadedMs = submission.UploadMs;
        double submitMs = submission.SubmitMs;

        // 不將提交與收取之間的流水線空檔計入本組；只量測同步、讀回及結果整理。
        var phase = Stopwatch.StartNew();
        double syncMs = 0d;
        if (!submission.ExternallySynchronized)
        {
            _rd.Sync();
            syncMs = phase.Elapsed.TotalMilliseconds;
        }
        token.ThrowIfCancellationRequested();

        long combinedReadStarted = Stopwatch.GetTimestamp();
        byte[] combinedBytes = _rd.BufferGetData(_batchHeaderBank, 0,
            checked((uint)(count * NeowCombinedReadbackTransport.BankBytes)));
        int slotBytes = NeowCombinedReadbackTransport.BankBytes;
        if (combinedBytes.Length != count * slotBytes)
            throw new InvalidDataException(
                $"N.BatchMergeReadLengthMismatch:expected={count * slotBytes};actual={combinedBytes.Length}");
        double combinedReadCallMs = Stopwatch.GetElapsedTime(combinedReadStarted).TotalMilliseconds;

        var headers = new uint[count][];
        var survivorCounts = new int[count];
        var groupsPerWindow = new uint[count];
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> slot = combinedBytes.AsSpan(index * slotBytes,
                NeowCombinedReadbackTransport.BankBytes);
            uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(
                slot[..NeowCombinedReadbackTransport.HeaderBytes].ToArray());
            ValidateBatchHeader(header, inputCounts[index], staged);
            headers[index] = header;
            survivorCounts[index] = checked((int)header[2]);
            groupsPerWindow[index] = (uint)((inputCounts[index] + 511) / 512);
        }

        // 先用前綴，再合併中型讀回；超大輸出維持逐窗口直接讀回。
        var payloadBytesByWindow = new byte[count][];
        var payloadFromPrefix = new bool[count];
        var payloadFromMedium = new bool[count];
        var payloadFromDirect = new bool[count];
        uint mediumBytesTotal = 0;
        var mediumOffsets = new long[count];
        for (int index = 0; index < count; index++)
        {
            int survivors = survivorCounts[index];
            if (survivors == 0) { payloadBytesByWindow[index] = []; continue; }
            if (survivors <= NeowCombinedReadbackTransport.PrefixCapacity)
            {
                ReadOnlySpan<byte> slot = combinedBytes.AsSpan(index * slotBytes,
                    NeowCombinedReadbackTransport.BankBytes);
                payloadBytesByWindow[index] = slot.Slice(NeowCombinedReadbackTransport.HeaderBytes,
                    survivors * sizeof(uint)).ToArray();
                payloadFromPrefix[index] = true;
            }
            else if (survivors <= NeowMediumReadbackTransport.CandidateCapacity)
            {
                mediumOffsets[index] = mediumBytesTotal;
                mediumBytesTotal = checked(mediumBytesTotal + (uint)(survivors * sizeof(uint)));
                payloadFromMedium[index] = true;
            }
            else payloadFromDirect[index] = true;
        }
        double mediumReadCallMs = 0;
        byte[]? mediumBytes = null;
        if (mediumBytesTotal > 0)
        {
            for (int index = 0; index < count; index++)
            {
                if (!payloadFromMedium[index]) continue;
                if (_rd.BufferCopy(_batchOutput[index], _batchMediumBank, 0,
                        checked((uint)mediumOffsets[index]),
                        checked((uint)(survivorCounts[index] * sizeof(uint)))) != Error.Ok)
                    throw new InvalidOperationException("N.BatchMergeMediumCopyFailed");
            }
            long mediumReadStarted = Stopwatch.GetTimestamp();
            mediumBytes = _rd.BufferGetData(_batchMediumBank, 0, mediumBytesTotal);
            if (mediumBytes.Length != mediumBytesTotal)
                throw new InvalidDataException(
                    $"N.BatchMergeMediumLengthMismatch:expected={mediumBytesTotal};actual={mediumBytes.Length}");
            mediumReadCallMs = Stopwatch.GetElapsedTime(mediumReadStarted).TotalMilliseconds;
        }
        for (int index = 0; index < count; index++)
        {
            if (payloadFromMedium[index])
                payloadBytesByWindow[index] = mediumBytes!.AsSpan(
                    checked((int)mediumOffsets[index]), survivorCounts[index] * sizeof(uint)).ToArray();
        }
        double directReadCallMs = 0;
        for (int index = 0; index < count; index++)
        {
            if (!payloadFromDirect[index]) continue;
            long directReadStarted = Stopwatch.GetTimestamp();
            payloadBytesByWindow[index] = NeowReadbackTransport.ReadDirect(
                _rd, _batchOutput[index], checked((uint)survivorCounts[index]));
            directReadCallMs += Stopwatch.GetElapsedTime(directReadStarted).TotalMilliseconds;
        }
        double readCostMs = phase.Elapsed.TotalMilliseconds - syncMs;

        var results = new FamilyCandidateSet[count];
        var payloadReadCallCounts = new int[count];
        var readbackByteCounts = new long[count];
        for (int index = 0; index < count; index++)
        {
            ulong[] ordinals = MaterializeBatchOrdinals(payloadBytesByWindow[index]);
            results[index] = FamilyCandidateSet.FromSortedAbi1(inputs[index].Batch, ordinals);
            payloadReadCallCounts[index] = survivorCounts[index] > 0 && !payloadFromPrefix[index] ? 1 : 0;
            readbackByteCounts[index] = slotBytes +
                                        (payloadReadCallCounts[index] > 0
                                            ? (long)survivorCounts[index] * sizeof(uint) : 0);
        }
        double materializeMs = phase.Elapsed.TotalMilliseconds - syncMs - readCostMs;
        // 共用提交耗時平均分攤，保證逐批加總等於群組實測值。
        double canonicalTotalMs = uploadedMs + submitMs + syncMs + readCostMs + materializeMs;
        var metrics = new NeowFamilyGpuMetrics[count];
        bool mediumSourceOnFirst = mediumBytesTotal > 0;
        for (int index = 0; index < count; index++)
        {
            uint[] header = headers[index];
            metrics[index] = new(
                checked((int)header[4]), results[index].Count, groupsPerWindow[index], readbackByteCounts[index],
                canonicalTotalMs / count, uploadedMs / count,
                (submitMs + syncMs) / count, readCostMs / count, materializeMs / count,
                groupsPerWindow[index] == 0 ? 0 : staged ? 3 : 1,
                header.Length >= 8 ? header[6] : 0, header.Length >= 8 ? header[7] : 0,
                0, 0, 0, 0, 0)
            {
                SubmitMs = submitMs / count,
                SyncMs = syncMs / count,
                HeaderReadCallMs = 0,
                HeaderReadCalls = 0,
                PayloadReadCallMs = (mediumReadCallMs + directReadCallMs) / count,
                PayloadReadCalls = payloadReadCallCounts[index],
                SmallPayloadReadCalls = 0,
                DirectPayloadReadCalls = payloadFromDirect[index] ? 1 : 0,
                // 逐窗口來源容量仍按前綴／256 KiB／直接讀回計算；共用緩衝容量另記。
                PayloadReadSourceCapacityBytes = payloadFromMedium[index]
                    ? NeowMediumReadbackTransport.BankBytes
                    : payloadFromDirect[index] ? (long)Capacity * sizeof(uint) : 0,
                CombinedReadCallMs = index == 0 ? combinedReadCallMs : 0,
                CombinedReadCalls = index == 0 ? 1 : 0,
                CombinedPayloadBatches = payloadFromPrefix[index] ? 1 : 0,
                LogicalPayloadBytes = (long)survivorCounts[index] * sizeof(uint),
                MediumPayloadReadCalls = payloadFromMedium[index] ? 1 : 0,
                MediumReadCallMs = index == 0 ? mediumReadCallMs : 0,
                MediumReadSourceCapacityBytes = index == 0 && mediumSourceOnFirst
                    ? (long)count * NeowMediumReadbackTransport.BankBytes : 0,
                CombinedReadSourceCapacityBytes = index == 0
                    ? (long)count * NeowCombinedReadbackTransport.BankBytes : 0,
                BatchMergeWindow = true,
                BatchMergeGroupSize = count
            };
        }
        _pendingBatch = null;
        return (results, metrics);
    }

    // 中止時先同步已提交的群組，再重用或釋放裝置；結果丟棄。
    internal void AbortPendingBatch()
    {
        if (_pendingBatch is null) return;
        try
        {
            // epoch 自行管理提交與同步；只有獨立批次才需在此排空，避免無提交時呼叫 Sync。
            if (!_pendingBatch.ExternallySynchronized) _rd.Sync();
        }
        catch (Exception ex)
        {
            Bootstrap.RuntimeLog.TryBackgroundWarning(
                $"neowBatchAbortSyncFailure=true;reason={ex.GetType().Name}:{ex.Message}");
        }
        finally { _pendingBatch = null; }
    }

    private Rid[] BatchWindowBuffers(int index, bool staged)
    {
        // 綁定順序沿用單窗口；只替換窗口自有的中繼資料、標頭、輸出與檢查點。
        var buffers = new List<Rid>(13)
        {
            _batchMeta[index], _buffers[1], _buffers[2], _buffers[3], _buffers[4], _buffers[5],
            _batchHeader[index], _batchOutput[index], _buffers[8], _buffers[9], _buffers[10]
        };
        if (staged)
        {
            buffers.Add(_batchCurse[index]);
            buffers.Add(_batchPair[index]);
        }
        return buffers.ToArray();
    }

    private void ValidateBatchHeader(uint[] header, int inputCount, bool staged)
    {
        if (header.Length != (_bonesKMode != 0 ? 16 : _bonesArcane ? 12 : 8) ||
            header[0] != 0x4e464d52 || header[1] != 1 || header[5] != _tag ||
            header[3] != 0 || header[4] != inputCount || header[2] > header[4] ||
            (staged && (header[6] > NeowFamilyGpuPlan.CurseCapacity ||
                        header[7] > NeowFamilyGpuPlan.PairCapacity || header[7] > header[6])))
            throw new InvalidDataException("N.BatchMergeHeaderInvalid");
    }

    // 沿用單窗口整理順序：先完成排列，再擴寬、排序並產生 ABI1。
    private static ulong[] MaterializeBatchOrdinals(byte[] payloadBytes)
    {
        uint[] packed = payloadBytes.Length == 0
            ? []
            : FamilyGpuComputeUtility.FromUInt32Bytes(payloadBytes);
        bool packedRadix = packed.Length >= 65536;
        if (packedRadix) SortLargePackedOrdinals(packed);
        ulong[] ordinals;
        if (packedRadix)
        {
            ordinals = new ulong[packed.Length];
            for (int index = 0; index < packed.Length; index++) ordinals[index] = packed[index];
        }
        else ordinals = packed.Select(value => (ulong)value).ToArray();
        if (!packedRadix && ordinals.Length > 1) Array.Sort(ordinals);
        return ordinals;
    }
}
