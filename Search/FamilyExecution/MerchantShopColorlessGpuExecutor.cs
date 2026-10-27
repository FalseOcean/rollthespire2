using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct MerchantShopColorlessGpuBatchMetrics(
    MerchantShopColorlessCompactionPath CompactionPath,
    int InputCandidates,
    int ProcessedCandidates,
    int Survivors,
    double UploadMs,
    double CommandMs,
    double SubmitMs,
    double SyncMs,
    double HeaderReadbackMs,
    double PayloadReadbackMs,
    double TypedAllocationMs,
    double DecodePayloadMs,
    double SortMs,
    double ValidationCandidateSetMs,
    long PayloadReadbackAllocatedBytes,
    long HostAllocatedBytes,
    long HeaderReadbackBytes,
    long PayloadReadbackBytes,
    double StablePhase1CommandMs,
    double StablePhase1SubmitMs,
    double StablePhase1SyncMs,
    double StableCountsReadbackMs,
    double StableHostPrefixMs,
    double StableOffsetsUploadMs,
    double StableScatterCommandMs,
    double StableScatterSubmitMs,
    double StableScatterSyncMs,
    long StableCountsReadbackBytes,
    double CanonicalAbi1ReadyMs,
    int Dispatches = 0)
{
    public double ReadbackMs => HeaderReadbackMs + PayloadReadbackMs + StableCountsReadbackMs;
    public double HostMaterializeMs => TypedAllocationMs + DecodePayloadMs + SortMs + ValidationCandidateSetMs;
    public long ReadbackBytes => HeaderReadbackBytes + PayloadReadbackBytes + StableCountsReadbackBytes;
    public double PhysicalMs => UploadMs + CommandMs + SubmitMs + SyncMs + ReadbackMs + StableHostPrefixMs + StableOffsetsUploadMs;
    public double CandidatesPerSecond => PhysicalMs <= 0d ? 0d : InputCandidates * 1000d / PhysicalMs;
}

/// <summary>
/// Family-specific GPU resources and synchronous batch execution. The unified
/// FamilyExecutionContext guarantees that every method is called on its single
/// session GPU owner thread; this class creates no thread, Task, or queue.
/// </summary>
internal sealed partial class MerchantShopColorlessGpuExecutor : IDisposable
{
    internal const int WorkgroupSize = 64;
    internal const int SeedsPerInvocation = 8;
    internal const int Capacity = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
    internal const int CompactInputCapacity = 1 << 22;
    internal const int SurvivorCapacity = 1 << 22;
    internal const int MaximumInvocationCount = Capacity / SeedsPerInvocation;
    internal const int MaximumWorkgroupCount = Capacity / (WorkgroupSize * SeedsPerInvocation);
    internal const int StableKeepMaskBytes = MaximumInvocationCount * sizeof(uint);
    internal const int StableBlockCountBytes = MaximumWorkgroupCount * sizeof(uint);
    internal const int StableBlockOffsetBytes = MaximumWorkgroupCount * sizeof(uint);

    private const uint HeaderMagic = 0x53464D52u; // "SFMR"
    private const uint HeaderAbi = 1u;
    private const int HeaderUIntCount = 8;
    private const string ShaderSuffix = "MerchantShopColorless.comp.glsl";
    private const string StableScatterShaderSuffix = "MerchantShopColorlessStableScatter.comp.glsl";

    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid _pipeline;
    private readonly Rid _batchMeta;
    private readonly Rid _inputOrdinals;
    private readonly Rid _outputHeader;
    private readonly Rid _outputOrdinals;
    private readonly Rid _uniformSet;
    private readonly bool _stableEnabled;
    private readonly Rid _stablePhase1Pipeline;
    private readonly Rid _stablePhase1UniformSet;
    private readonly Rid _stableScatterPipeline;
    private readonly Rid _stableScatterUniformSet;
    private readonly Rid _stableKeepMasks;
    private readonly Rid _stableBlockCounts;
    private readonly Rid _stableBlockOffsets;
    private readonly uint _planTag;
    private readonly string _performanceSuffix;
    private readonly int _ownerThreadId;
    private readonly byte[] _metadataBytes = new byte[6 * sizeof(uint)];
    private readonly byte[] _headerResetBytes;
    private bool _disposed;
    private PrivateOrdinalBuffer? _privateInput, _privateOutput;
    private int OutputCapacity => _privateInput is not null || _privateOutput is not null ? Capacity : SurvivorCapacity;

    private MerchantShopColorlessGpuExecutor(
        RenderingDevice rd,
        Rid pipeline,
        Rid batchMeta,
        Rid inputOrdinals,
        Rid outputHeader,
        Rid outputOrdinals,
        Rid uniformSet,
        bool stableEnabled,
        Rid stablePhase1Pipeline,
        Rid stablePhase1UniformSet,
        Rid stableScatterPipeline,
        Rid stableScatterUniformSet,
        Rid stableKeepMasks,
        Rid stableBlockCounts,
        Rid stableBlockOffsets,
        uint planTag,
        int ownerThreadId,
        double setupMs,
        string deviceName,
        string performanceSuffix)
    {
        _rd = rd;
        _pipeline = pipeline;
        _batchMeta = batchMeta;
        _inputOrdinals = inputOrdinals;
        _outputHeader = outputHeader;
        _outputOrdinals = outputOrdinals;
        _uniformSet = uniformSet;
        _stableEnabled = stableEnabled;
        _stablePhase1Pipeline = stablePhase1Pipeline;
        _stablePhase1UniformSet = stablePhase1UniformSet;
        _stableScatterPipeline = stableScatterPipeline;
        _stableScatterUniformSet = stableScatterUniformSet;
        _stableKeepMasks = stableKeepMasks;
        _stableBlockCounts = stableBlockCounts;
        _stableBlockOffsets = stableBlockOffsets;
        _planTag = planTag;
        _headerResetBytes = FamilyGpuComputeUtility.ToBytes(CreateHeader(planTag));
        _ownerThreadId = ownerThreadId;
        SetupMs = setupMs;
        DeviceName = deviceName;
        _performanceSuffix = performanceSuffix;
    }

    public double SetupMs { get; }
    public string DeviceName { get; }
    public bool StableOrderedCompactionEnabled => _stableEnabled;
    public string PhysicalImplementationRevision => (_stableEnabled
        ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessStableOrderedCompaction
        : FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicAppendHostSort) + _performanceSuffix;

    public static MerchantShopColorlessGpuExecutor Create(RenderingDevice rd, ExactSearchExecutionRequest request,
        MerchantShopColorlessCompactionPath compactionPath) => Create(rd, request, compactionPath, null, null);
    internal static MerchantShopColorlessGpuExecutor Create(
        RenderingDevice rd,
        ExactSearchExecutionRequest request,
        MerchantShopColorlessCompactionPath compactionPath,
        PrivateOrdinalBuffer? privateInput, PrivateOrdinalBuffer? privateOutput)
    {
        if (privateInput is not null && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("S.PrivateBuffersAlias");
        if ((privateInput is not null || privateOutput is not null) && compactionPath == MerchantShopColorlessCompactionPath.StableOrderedCompaction)
            throw new ArgumentException("S.PrivateAppendRequiresAtomicPath");
        ArgumentNullException.ThrowIfNull(rd);
        ArgumentNullException.ThrowIfNull(request);
        var timer = Stopwatch.StartNew();
        var owned = new List<Rid>();
        try
        {
            Rid Add(Rid rid) { owned.Add(rid); return rid; }
            bool stableEnabled = compactionPath == MerchantShopColorlessCompactionPath.StableOrderedCompaction;
            Beta111MerchantColorlessAuthority authority = Beta111MerchantColorlessAuthority.From(request.Authority);
            string performanceSuffix = PartyEarlyRejectionRevision(request.Evaluation, authority.PlayersCount);
            uint[] plan = PackPlan(request.Evaluation, authority, out uint[] targets, out string identityDefines);
            uint planTag = StableTag(
                request.SnapshotFingerprint + "|" + (stableEnabled
                    ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessStableOrderedCompaction
                    : FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicAppendHostSort) + performanceSuffix);

            Rid batchMeta = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, 6 * sizeof(uint)));
            Rid inputOrdinals = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(
                rd, CompactInputCapacity * 2 * sizeof(uint)));
            Rid planBuffer = Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytes(plan)));
            Rid targetBuffer = Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(targets)));
            Rid outputHeader = Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd,
                FamilyGpuComputeUtility.ToBytes(CreateHeader(planTag))));
            Rid outputOrdinals = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(
                rd, (privateInput is null ? SurvivorCapacity : Capacity) * 2 * sizeof(uint)));

            string source = FamilyGpuComputeUtility.LoadFamilyShaderWithVisibleSeedRootHash(ShaderSuffix);
            source = ConfigureEarlyRejection(source, request.Evaluation, request.Authority.PlayersCount);
            if (identityDefines.Length != 0) source = source.Insert("#version 450".Length, identityDefines);
            // Addressing/emission only; reuse the existing merchant predicate and RNG body.
            if (privateInput is not null)
            {
                const string read = "make_u64(compact_input.values[input_index * 2u], compact_input.values[input_index * 2u + 1u])";
                if (!source.Contains(read, StringComparison.Ordinal)) throw new InvalidOperationException("S.PrivateInputSeamChanged");
                source = source.Replace(read, "uint64_t(compact_input.values[input_index])", StringComparison.Ordinal)
                    .Replace("if (logical_ordinal >= uint64_t(batch_meta.values[2u])) return;",
                        "if (logical_ordinal >= uint64_t(batch_meta.values[2u])) { atomicExchange(output_header.values[3u],2u); return; }", StringComparison.Ordinal);
            }
            if (privateOutput is not null)
            {
                const string low = "compact_output.values[slot * 2u] = uint(logical_ordinal);";
                const string high = "compact_output.values[slot * 2u + 1u] = uint(logical_ordinal >> 32u);";
                if (!source.Contains(low, StringComparison.Ordinal) || !source.Contains(high, StringComparison.Ordinal)) throw new InvalidOperationException("S.PrivateOutputSeamChanged");
                source = source.Replace(low, "compact_output.values[slot] = uint(logical_ordinal);", StringComparison.Ordinal).Replace(high, "", StringComparison.Ordinal);
            }
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "MerchantShopColorlessFamilyS"));
            Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "S.MerchantShopColorless"));
            if (!rd.ComputePipelineIsValid(pipeline))
                throw new InvalidOperationException("SFamilyGpuPipelineInvalid");
            Rid uniformSet = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, new[]
            {
                batchMeta, inputOrdinals, planBuffer, targetBuffer, outputHeader, outputOrdinals
            }));

            Rid stablePhase1Pipeline = default;
            Rid stablePhase1UniformSet = default;
            Rid stableScatterPipeline = default;
            Rid stableScatterUniformSet = default;
            Rid stableKeepMasks = default;
            Rid stableBlockCounts = default;
            Rid stableBlockOffsets = default;
            if (stableEnabled)
            {
                stableKeepMasks = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, StableKeepMaskBytes));
                stableBlockCounts = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, StableBlockCountBytes));
                stableBlockOffsets = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, StableBlockOffsetBytes));

                string stablePhase1Source = EnableStableOrderedCompaction(source);
                Rid stablePhase1Shader = Add(FamilyGpuComputeUtility.CompileShader(
                    rd, stablePhase1Source, "MerchantShopColorlessFamilySStablePhase1"));
                stablePhase1Pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, stablePhase1Shader, "S.StablePhase1"));
                if (!rd.ComputePipelineIsValid(stablePhase1Pipeline))
                    throw new InvalidOperationException("SFamilyStablePhase1PipelineInvalid");
                stablePhase1UniformSet = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, stablePhase1Shader, new[]
                {
                    batchMeta, inputOrdinals, planBuffer, targetBuffer, outputHeader, outputOrdinals,
                    stableKeepMasks, stableBlockCounts
                }));

                string scatterSource = FamilyGpuComputeUtility.LoadEmbeddedShader(StableScatterShaderSuffix);
                Rid scatterShader = Add(FamilyGpuComputeUtility.CompileShader(
                    rd, scatterSource, "MerchantShopColorlessFamilySStableScatter"));
                stableScatterPipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, scatterShader, "S.StableScatter"));
                if (!rd.ComputePipelineIsValid(stableScatterPipeline))
                    throw new InvalidOperationException("SFamilyStableScatterPipelineInvalid");
                stableScatterUniformSet = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, scatterShader, new[]
                {
                    batchMeta, stableKeepMasks, stableBlockOffsets, outputHeader, outputOrdinals
                }));
            }

            timer.Stop();
            var result = new MerchantShopColorlessGpuExecutor(
                rd, pipeline, batchMeta, inputOrdinals, outputHeader, outputOrdinals,
                uniformSet, stableEnabled, stablePhase1Pipeline, stablePhase1UniformSet,
                stableScatterPipeline, stableScatterUniformSet, stableKeepMasks, stableBlockCounts,
                stableBlockOffsets, planTag, Thread.CurrentThread.ManagedThreadId,
                timer.Elapsed.TotalMilliseconds, rd.GetDeviceName()?.Trim() ?? "unknown", performanceSuffix);
            result._privateInput = privateInput; result._privateOutput = privateOutput;
            result._owned.AddRange(owned);
            owned.Clear();
            return result;
        }
        catch (Exception ex)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(ex,
                () => FamilyGpuComputeUtility.FreeAll(rd, owned), "S.Initialize");
            throw;
        }
    }

    public FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        if (_privateInput is not null || _privateOutput is not null) throw new InvalidOperationException("S.PrivateEntryRequired");
        // Public output has the same bounded capacity as compact upload. Dense
        // inputs also need windows: legal small/modded pools can pass every root.
        if (input.Count > SurvivorCapacity && input.Count <= Capacity)
            return ExecutePublicWindows(input, token, out metrics);
        return ExecuteCore(input, input.Count, token, out metrics)!;
    }
    internal FamilyCandidateSet ExecutePrivate(SearchBatch batch, int count, CancellationToken token, out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        if (_privateInput is null || _privateOutput is not null) throw new InvalidOperationException("S.PrivateTerminalPortsRequired");
        _privateInput.CheckPopulation(batch, count);
        return ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics)!;
    }
    internal int ExecutePrivateStage(SearchBatch batch, int count, CancellationToken token, out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        if (_privateOutput is null || (_privateInput is null && count != batch.BatchCandidateCount)) throw new InvalidOperationException("S.PrivateStagePortsRequired");
        _privateOutput.CheckPopulation(batch, count); _privateInput?.CheckPopulation(batch, count);
        ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics);
        return metrics.Survivors;
    }
    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, int inputCount, CancellationToken cancellationToken, out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (inputCount == 0)
        {
            metrics = new MerchantShopColorlessGpuBatchMetrics();
            return FamilyCandidateSet.FromSortedAbi1(input.Batch, Array.Empty<ulong>());
        }
        if (inputCount > Capacity)
            throw new InvalidOperationException("SFamilyGpuInputExceedsPhysicalBatch");
        if (!input.IsDense && inputCount > CompactInputCapacity)
            throw new InvalidOperationException("SFamilyCompactInputExceedsTransferCapacity");
        if (_stableEnabled && input.IsDense)
            return ExecuteStableOrdered(input, cancellationToken, out metrics);

        var canonicalTimer = Stopwatch.StartNew();
        var timer = Stopwatch.StartNew();
        Span<uint> meta = FamilyGpuComputeUtility.UInt32Span(_metadataBytes);
        meta[0] = unchecked((uint)input.Batch.BatchBase); meta[1] = unchecked((uint)(input.Batch.BatchBase >> 32));
        meta[2] = checked((uint)input.Batch.BatchCandidateCount); meta[3] = checked((uint)inputCount);
        meta[4] = input.IsDense && _privateInput is null ? 0u : 1u; meta[5] = (uint)OutputCapacity;
        FamilyGpuComputeUtility.UpdateBytes(_rd, _batchMeta, _metadataBytes, "SFamilyBatchMeta");
        FamilyGpuComputeUtility.UpdateBytes(_rd, _outputHeader, _headerResetBytes, "SFamilyOutputHeader");
        if (!input.IsDense && _privateInput is null)
        {
            ReadOnlySpan<ulong> ordinals = input.ExportAbi1().Span;
            // S's public transport is genuinely two-word. Copy the complete
            // ABI1 words directly without a second typed staging allocation.
            byte[] packed = MemoryMarshal.AsBytes(ordinals).ToArray();
            FamilyGpuComputeUtility.UpdateBytes(_rd, _inputOrdinals, packed, "SFamilyCompactInput");
        }
        timer.Stop();
        double uploadMs = timer.Elapsed.TotalMilliseconds;

        cancellationToken.ThrowIfCancellationRequested();
        timer.Restart();
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        int candidatesPerGroup = WorkgroupSize * SeedsPerInvocation;
        uint groups = checked((uint)((inputCount + candidatesPerGroup - 1) / candidatesPerGroup));
        _rd.ComputeListDispatch(list, groups, 1u, 1u);
        _rd.ComputeListEnd();
        timer.Stop();
        double commandMs = timer.Elapsed.TotalMilliseconds;

        (double submitMs, double syncMs) = FamilyGpuComputeUtility.SubmitAndSync(_rd, "S.MerchantShopColorless");
        cancellationToken.ThrowIfCancellationRequested();

        timer.Restart();
        byte[] rawHeader = _rd.BufferGetData(_outputHeader, 0u, HeaderUIntCount * sizeof(uint));
        ReadOnlySpan<uint> header = FamilyGpuComputeUtility.UInt32Span(rawHeader);
        int processedCount = ValidateHeader(header, inputCount);
        int survivorCount = checked((int)header[2]);
        timer.Stop();
        double headerReadbackMs = timer.Elapsed.TotalMilliseconds;

        if (_privateOutput is not null)
        {
            metrics = new MerchantShopColorlessGpuBatchMetrics
            {
                CompactionPath = MerchantShopColorlessCompactionPath.AtomicAppendHostSort,
                InputCandidates = inputCount, ProcessedCandidates = processedCount, Survivors = survivorCount, Dispatches = 1,
                UploadMs = uploadMs, CommandMs = commandMs, SubmitMs = submitMs, SyncMs = syncMs,
                HeaderReadbackMs = headerReadbackMs, HeaderReadbackBytes = rawHeader.LongLength,
                CanonicalAbi1ReadyMs = canonicalTimer.Elapsed.TotalMilliseconds
            };
            return null;
        }
        long payloadAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        byte[] rawOrdinals = survivorCount == 0
            ? Array.Empty<byte>()
            : _rd.BufferGetData(_outputOrdinals, 0u, checked((uint)(survivorCount * 2 * sizeof(uint))));
        timer.Stop();
        double payloadReadbackMs = timer.Elapsed.TotalMilliseconds;
        long payloadReadbackAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - payloadAllocationBefore;

        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var survivors = new ulong[survivorCount];
        timer.Stop();
        double typedAllocationMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        if (rawOrdinals.Length != checked(survivorCount * sizeof(ulong)))
            throw new InvalidDataException("SFamilyGpuPayloadLengthMismatch");
        if (rawOrdinals.Length > 0)
            Buffer.BlockCopy(rawOrdinals, 0, survivors, 0, rawOrdinals.Length);
        timer.Stop();
        double decodePayloadMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        if (survivors.Length > 1) Array.Sort(survivors);
        timer.Stop();
        double sortMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        FamilyCandidateSet result = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors);
        timer.Stop();
        double validationCandidateSetMs = timer.Elapsed.TotalMilliseconds;
        long hostAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        canonicalTimer.Stop();
        metrics = new MerchantShopColorlessGpuBatchMetrics(
            MerchantShopColorlessCompactionPath.AtomicAppendHostSort,
            inputCount, processedCount, survivors.Length,
            uploadMs, commandMs, submitMs, syncMs,
            headerReadbackMs, payloadReadbackMs,
            typedAllocationMs, decodePayloadMs, sortMs, validationCandidateSetMs,
            payloadReadbackAllocatedBytes, hostAllocatedBytes,
            rawHeader.LongLength, rawOrdinals.LongLength,
            0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0L,
            canonicalTimer.Elapsed.TotalMilliseconds, 1);
        return result;
    }

    private FamilyCandidateSet ExecuteStableOrdered(
        FamilyCandidateSet input,
        CancellationToken cancellationToken,
        out MerchantShopColorlessGpuBatchMetrics metrics)
    {
        if (!input.IsDense) throw new InvalidOperationException("SFamilyStableCompactionRequiresDenseInput");
        var canonicalTimer = Stopwatch.StartNew();
        var timer = Stopwatch.StartNew();
        Span<uint> meta = FamilyGpuComputeUtility.UInt32Span(_metadataBytes);
        meta[0] = unchecked((uint)input.Batch.BatchBase); meta[1] = unchecked((uint)(input.Batch.BatchBase >> 32));
        meta[2] = checked((uint)input.Batch.BatchCandidateCount); meta[3] = checked((uint)input.Count);
        meta[4] = 0u; meta[5] = SurvivorCapacity;
        FamilyGpuComputeUtility.UpdateBytes(_rd, _batchMeta, _metadataBytes, "SFamilyStableBatchMeta");
        FamilyGpuComputeUtility.UpdateBytes(_rd, _outputHeader, _headerResetBytes, "SFamilyStableOutputHeader");
        timer.Stop();
        double uploadMs = timer.Elapsed.TotalMilliseconds;

        int candidatesPerGroup = WorkgroupSize * SeedsPerInvocation;
        uint groups = checked((uint)((input.Count + candidatesPerGroup - 1) / candidatesPerGroup));
        cancellationToken.ThrowIfCancellationRequested();
        timer.Restart();
        long phase1List = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(phase1List, _stablePhase1Pipeline);
        _rd.ComputeListBindUniformSet(phase1List, _stablePhase1UniformSet, 0);
        _rd.ComputeListDispatch(phase1List, groups, 1u, 1u);
        _rd.ComputeListEnd();
        timer.Stop();
        double phase1CommandMs = timer.Elapsed.TotalMilliseconds;

        (double phase1SubmitMs, double phase1SyncMs) = FamilyGpuComputeUtility.SubmitAndSync(_rd, "S.StablePhase1");
        cancellationToken.ThrowIfCancellationRequested();

        timer.Restart();
        byte[] phase1RawHeader = _rd.BufferGetData(_outputHeader, 0u, HeaderUIntCount * sizeof(uint));
        ReadOnlySpan<uint> phase1Header = FamilyGpuComputeUtility.UInt32Span(phase1RawHeader);
        int processedCount = ValidateHeader(phase1Header, input.Count);
        int survivorCount = checked((int)phase1Header[2]);
        timer.Stop();
        double phase1HeaderReadbackMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        byte[] rawCounts = _rd.BufferGetData(_stableBlockCounts, 0u, checked(groups * sizeof(uint)));
        if (rawCounts.Length != checked((int)groups * sizeof(uint)))
            throw new InvalidDataException("SFamilyStableBlockCountLengthMismatch");
        ReadOnlySpan<uint> counts = FamilyGpuComputeUtility.UInt32Span(rawCounts);
        timer.Stop();
        double countsReadbackMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        var offsetBytes = new byte[checked(counts.Length * sizeof(uint))];
        Span<uint> offsets = FamilyGpuComputeUtility.UInt32Span(offsetBytes);
        ulong total = 0UL;
        for (int index = 0; index < counts.Length; index++)
        {
            offsets[index] = checked((uint)total);
            total += counts[index];
            if (total > SurvivorCapacity)
                throw new InvalidDataException("SFamilyStablePrefixExceedsSurvivorCapacity");
        }
        if (total != checked((uint)survivorCount))
        {
            throw new InvalidDataException(
                $"SFamilyStablePrefixCountMismatch:prefix={total};header={survivorCount};blocks={counts.Length}");
        }
        timer.Stop();
        double hostPrefixMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        FamilyGpuComputeUtility.UpdateBytes(_rd, _stableBlockOffsets, offsetBytes, "SFamilyStableBlockOffsets");
        timer.Stop();
        double offsetsUploadMs = timer.Elapsed.TotalMilliseconds;
        cancellationToken.ThrowIfCancellationRequested();

        timer.Restart();
        long scatterList = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(scatterList, _stableScatterPipeline);
        _rd.ComputeListBindUniformSet(scatterList, _stableScatterUniformSet, 0);
        _rd.ComputeListDispatch(scatterList, groups, 1u, 1u);
        _rd.ComputeListEnd();
        timer.Stop();
        double scatterCommandMs = timer.Elapsed.TotalMilliseconds;

        (double scatterSubmitMs, double scatterSyncMs) = FamilyGpuComputeUtility.SubmitAndSync(_rd, "S.StableScatter");
        cancellationToken.ThrowIfCancellationRequested();

        timer.Restart();
        byte[] finalRawHeader = _rd.BufferGetData(_outputHeader, 0u, HeaderUIntCount * sizeof(uint));
        ReadOnlySpan<uint> finalHeader = FamilyGpuComputeUtility.UInt32Span(finalRawHeader);
        ValidateHeader(finalHeader, input.Count);
        if (checked((int)finalHeader[2]) != survivorCount)
            throw new InvalidDataException("SFamilyStableScatterChangedSurvivorCount");
        timer.Stop();
        double finalHeaderReadbackMs = timer.Elapsed.TotalMilliseconds;

        long payloadAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        byte[] rawOrdinals = survivorCount == 0
            ? Array.Empty<byte>()
            : _rd.BufferGetData(_outputOrdinals, 0u, checked((uint)(survivorCount * 2 * sizeof(uint))));
        timer.Stop();
        double payloadReadbackMs = timer.Elapsed.TotalMilliseconds;
        long payloadReadbackAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - payloadAllocationBefore;

        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var survivors = new ulong[survivorCount];
        timer.Stop();
        double typedAllocationMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        if (rawOrdinals.Length != checked(survivorCount * sizeof(ulong)))
            throw new InvalidDataException("SFamilyGpuPayloadLengthMismatch");
        if (rawOrdinals.Length > 0)
            Buffer.BlockCopy(rawOrdinals, 0, survivors, 0, rawOrdinals.Length);
        timer.Stop();
        double decodePayloadMs = timer.Elapsed.TotalMilliseconds;

        // Stable scatter is constructive; the retained O(N) ABI1 validator below
        // remains the fail-closed range/order/duplicate authority for this experiment.
        timer.Restart();
        FamilyCandidateSet result = FamilyCandidateSet.FromSortedAbi1(input.Batch, survivors);
        timer.Stop();
        double validationCandidateSetMs = timer.Elapsed.TotalMilliseconds;
        long hostAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        canonicalTimer.Stop();

        metrics = new MerchantShopColorlessGpuBatchMetrics(
            MerchantShopColorlessCompactionPath.StableOrderedCompaction,
            input.Count, processedCount, survivors.Length,
            uploadMs,
            phase1CommandMs + scatterCommandMs,
            phase1SubmitMs + scatterSubmitMs,
            phase1SyncMs + scatterSyncMs,
            phase1HeaderReadbackMs + finalHeaderReadbackMs,
            payloadReadbackMs,
            typedAllocationMs,
            decodePayloadMs,
            0d,
            validationCandidateSetMs,
            payloadReadbackAllocatedBytes,
            hostAllocatedBytes,
            phase1RawHeader.LongLength + finalRawHeader.LongLength,
            rawOrdinals.LongLength,
            phase1CommandMs,
            phase1SubmitMs,
            phase1SyncMs,
            countsReadbackMs,
            hostPrefixMs,
            offsetsUploadMs,
            scatterCommandMs,
            scatterSubmitMs,
            scatterSyncMs,
            rawCounts.LongLength,
            canonicalTimer.Elapsed.TotalMilliseconds, 2);
        return result;
    }

    public void Dispose()
    {
        AssertOwnerThread();
        if (_disposed) return;
        _disposed = true;
        // Execute completes each Submit/Sync pair before returning.
        FamilyGpuComputeUtility.FreeAll(_rd, _owned);
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("SFamilyGpuOwnerThreadMismatch");
    }

    private int ValidateHeader(ReadOnlySpan<uint> header, int maximumProcessed)
    {
        if (header.Length < HeaderUIntCount || header[0] != HeaderMagic || header[1] != HeaderAbi || header[5] != _planTag)
            throw new InvalidDataException("SFamilyGpuHeaderIdentityMismatch");
        int output = checked((int)header[2]);
        int overflow = checked((int)header[3]);
        int processed = checked((int)header[4]);
        if (overflow != 0 || output < 0 || output > processed || processed != maximumProcessed || output > OutputCapacity)
        {
            throw new InvalidDataException(
                $"SFamilyGpuHeaderInvalid:output={output};processed={processed};max={maximumProcessed};" +
                $"overflow={overflow};survivorCapacity={SurvivorCapacity}");
        }
        return processed;
    }

    private static uint[] CreateHeader(uint planTag) =>
        [HeaderMagic, HeaderAbi, 0u, 0u, 0u, planTag, 0u, 0u];

    private static string EnableStableOrderedCompaction(string source)
    {
        const string version = "#version 450";
        if (!source.StartsWith(version, StringComparison.Ordinal))
            throw new InvalidOperationException("SFamilyStableShaderVersionMarkerMissing");
        return source.Insert(version.Length, "\n#define RT2_S_STABLE_ORDERED_COMPACTION 1");
    }

    internal static string PartyEarlyRejectionRevision(ExactSearchEvaluationProjection evaluation, int playersCount) =>
        playersCount > 1 && HasEarlyRejectionOpportunity(evaluation, playersCount)
            ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessPartyPrefix : "";

    private static bool HasEarlyRejectionOpportunity(ExactSearchEvaluationProjection evaluation, int playersCount)
    {
        if (playersCount is < 1 or > 4) return false;
        int earliestFailure = int.MaxValue, lastObservation = 0;
        foreach (var condition in evaluation.MerchantColorlessConditions)
        {
            lastObservation = Math.Max(lastObservation, condition.MerchantOrdinal);
            earliestFailure = Math.Min(earliestFailure, condition.MerchantOrdinal);
        }
        foreach (var condition in evaluation.MerchantColorlessSequenceConditions)
        {
            lastObservation = Math.Max(lastObservation, condition.Count);
            if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
            {
                for (int i = 0; i < condition.Slots.Count; i++)
                {
                    if (!condition.Slots[i].HasValue) continue;
                    earliestFailure = Math.Min(earliestFailure, i + 1);
                    break;
                }
            }
            else if (condition.OrderMode == CombatRewardSequenceOrderMode.Unordered)
            {
                // Every non-wildcard is an obligation, including repeated identities.
                int required = condition.Slots.Count(slot => slot.HasValue);
                earliestFailure = Math.Min(earliestFailure, condition.Count - required + 1);
            }
        }
        return earliestFailure < lastObservation;
    }

    internal static string ConfigureEarlyRejection(string source, ExactSearchEvaluationProjection evaluation, int playersCount)
    {
        const string version = "#version 450";
        if (!source.StartsWith(version, StringComparison.Ordinal))
            throw new InvalidOperationException("SFamilyEarlyRejectionShaderVersionMarkerMissing");
        // S family admission owns exact pool and slot inputs for one to four
        // players. Each child keeps its own root+slot Shops stream; only shapes
        // with an opportunity before the final merchant use the streaming body.
        return source.Insert(version.Length, "\n#define RT2_S_EARLY_REJECTION " +
            (HasEarlyRejectionOpportunity(evaluation, playersCount) ? "1" : "0") + "\n");
    }

    private static uint[] PackPlan(
        ExactSearchEvaluationProjection evaluation,
        Beta111MerchantColorlessAuthority authority,
        out uint[] targets,
        out string identityDefines)
    {
        ulong shopsHash = XxHash64.Hash("shops"u8, 0UL);
        var output = new List<uint> { 0u, 0u, 0u, 0u, 0u, 0u, 0u };
        var targetValues = new List<uint>();
        foreach (MerchantColorlessSlotCondition condition in evaluation.MerchantColorlessConditions)
        {
            IReadOnlyList<ModelKey> pool = condition.Slot == MerchantColorlessSlot.Uncommon
                ? authority.UncommonPool
                : authority.RarePool;
            output.Add(checked((uint)condition.MerchantOrdinal));
            output.Add(condition.Slot == MerchantColorlessSlot.Uncommon ? 0u : 1u);
            int target = IndexOf(pool, condition.TargetCardKey);
            output.Add(target < 0 ? uint.MaxValue - 1 : checked((uint)target));
        }
        foreach (MerchantColorlessSequenceSearchCondition condition in evaluation.MerchantColorlessSequenceConditions)
        {
            IReadOnlyList<ModelKey> pool = condition.Slot == MerchantColorlessSlot.Uncommon
                ? authority.UncommonPool
                : authority.RarePool;
            output.Add(condition.Slot == MerchantColorlessSlot.Uncommon ? 0u : 1u);
            output.Add(checked((uint)condition.Count));
            output.Add(condition.OrderMode == CombatRewardSequenceOrderMode.Ordered ? 0u : 1u);
            output.Add(checked((uint)targetValues.Count));
            output.Add(checked((uint)condition.Slots.Count));
            targetValues.AddRange(condition.Slots.Select(key =>
            {
                if (!key.HasValue) return uint.MaxValue;
                int target = IndexOf(pool, key.Value);
                return target < 0 ? uint.MaxValue - 1 : checked((uint)target);
            }));
        }
        // Draw from every original occurrence, then compare its ModelKey identity.
        // Unique pools retain the original shader body without an extra lookup.
        string PackCanonicalIndexes(IReadOnlyList<ModelKey> pool, string macro)
        {
            var first = new Dictionary<ModelKey, uint>(ModelKeyComparer.Instance);
            var canonical = new uint[pool.Count];
            bool duplicates = false;
            for (int i = 0; i < pool.Count; i++)
            {
                if (!first.TryGetValue(pool[i], out uint index)) first.Add(pool[i], index = checked((uint)i));
                else duplicates = true;
                canonical[i] = index;
            }
            if (!duplicates) return string.Empty;
            int offset = targetValues.Count;
            targetValues.AddRange(canonical);
            return "\n#define " + macro + " " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "u\n";
        }
        identityDefines = PackCanonicalIndexes(authority.UncommonPool, "RT2_S_CANONICAL_UNCOMMON_OFFSET") +
            PackCanonicalIndexes(authority.RarePool, "RT2_S_CANONICAL_RARE_OFFSET");
        output[0] = checked((uint)evaluation.MerchantColorlessConditions.Count);
        output[1] = checked((uint)evaluation.MerchantColorlessSequenceConditions.Count);
        output[2] = checked((uint)authority.UncommonPool.Count);
        output[3] = checked((uint)authority.RarePool.Count);
        output[4] = checked((uint)authority.PlayerSlotIndex);
        output[5] = unchecked((uint)shopsHash);
        output[6] = unchecked((uint)(shopsHash >> 32));
        targets = targetValues.ToArray();
        return output.ToArray();
    }

    private static int IndexOf(IReadOnlyList<ModelKey> values, ModelKey target)
    {
        for (int index = 0; index < values.Count; index++)
            if (values[index] == target) return index;
        return -1;
    }

    private static uint StableTag(string value) =>
        BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0);
}
